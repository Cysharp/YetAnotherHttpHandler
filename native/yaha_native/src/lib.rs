use std::{
    cell::RefCell,
    ptr::null,
    sync::{Arc, Mutex},
};

use hyper::{
    body::{Bytes, HttpBody},
    http::{HeaderName, HeaderValue},
    Body, Request, StatusCode, Version,
};

mod context;
mod interop;
mod primitives;

use context::{
    YahaNativeContext, YahaNativeContextInternal, YahaNativeRequestContext,
    YahaNativeRequestContextInternal,
};
use interop::{ByteBuffer, StringBuffer};
use primitives::YahaHttpVersion;

thread_local! {
    static LAST_ERROR: RefCell<Option<String>> = RefCell::new(None);
}

#[no_mangle]
pub extern "C" fn yaha_get_last_error() -> *const ByteBuffer {
    match LAST_ERROR.with(|p| p.borrow_mut().take()) {
        Some(e) => {
            let buf = ByteBuffer::from_vec(e.clone().into_bytes());
            Box::into_raw(Box::new(buf))
        }
        None => null(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn yaha_free_byte_buffer(s: *mut ByteBuffer) {
    let buf = Box::from_raw(s);
    buf.destroy();
}

#[no_mangle]
pub extern "C" fn yaha_init_runtime(
    on_status_code_and_headers_receive: extern "C" fn(
        req_seq: i32,
        status_code: i32,
        version: YahaHttpVersion,
    ),
    on_receive: extern "C" fn(req_seq: i32, length: usize, buf: *const u8),
    on_complete: extern "C" fn(req_seq: i32, has_error: u8),
) -> *mut YahaNativeContext {
    let ctx = Box::new(YahaNativeContextInternal::new(
        on_status_code_and_headers_receive,
        on_receive,
        on_complete,
    ));
    Box::into_raw(ctx) as *mut YahaNativeContext
}

#[no_mangle]
pub extern "C" fn yaha_dispose_runtime(ctx: *mut YahaNativeContext) -> () {
    let ctx = unsafe { Box::from_raw(ctx as *mut YahaNativeContextInternal) };
}

#[cfg(feature = "rustls")]
mod danger {
    pub struct NoCertificateVerification {}

    impl rustls::client::ServerCertVerifier for NoCertificateVerification {
        fn verify_server_cert(
            &self,
            _end_entity: &rustls::Certificate,
            _intermediates: &[rustls::Certificate],
            _server_name: &rustls::ServerName,
            _scts: &mut dyn Iterator<Item = &[u8]>,
            _ocsp: &[u8],
            _now: std::time::SystemTime,
        ) -> Result<rustls::client::ServerCertVerified, rustls::Error> {
            Ok(rustls::client::ServerCertVerified::assertion())
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_new(
    ctx: *const YahaNativeContext,
    seq: i32,
) -> *const YahaNativeRequestContext {
    let builder = Request::builder();

    let req_ctx = Arc::new(Mutex::new(YahaNativeRequestContextInternal {
        seq: seq,
        builder: Some(builder),
        sender: None,
        has_body: false,
        completed: false,

        response_version: YahaHttpVersion::Http10,
        response_trailers: None,
        response_headers: None,
        response_status: StatusCode::OK,
    }));
    Arc::into_raw(req_ctx) as *const YahaNativeRequestContext
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_method(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    value: *const StringBuffer,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    let builder = req_ctx.builder.take().unwrap();
    req_ctx.builder = Some(builder.method((*value).to_str()));
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_has_body(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    value: bool,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    req_ctx.has_body = value;
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_uri(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    value: *const StringBuffer,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    let builder = req_ctx.builder.take().unwrap();
    req_ctx.builder = Some(builder.uri((*value).to_str()));
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_version(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    value: YahaHttpVersion,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    let builder = req_ctx.builder.take().unwrap();
    req_ctx.builder = Some(builder.version(match value {
        YahaHttpVersion::Http09 => Version::HTTP_09,
        YahaHttpVersion::Http10 => Version::HTTP_10,
        YahaHttpVersion::Http11 => Version::HTTP_11,
        YahaHttpVersion::Http2 => Version::HTTP_2,
        YahaHttpVersion::Http3 => Version::HTTP_3,
    }));
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_header(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    key: *const StringBuffer,
    value: *const StringBuffer,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    // TODO: Handle invalid header values
    let builder = req_ctx.builder.take().unwrap();
    req_ctx.builder = Some(builder.header(
        HeaderName::from_bytes((*key).to_bytes()).unwrap(),
        HeaderValue::from_bytes((*value).to_bytes()).unwrap(),
    ));

    true
}

#[no_mangle]
pub extern "C" fn yaha_request_begin(
    ctx: *mut YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> bool {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);

    // Begin request on async runtime.
    let body;
    let runtime_handle = ctx.runtime.handle().clone();
    let req_ctx = context::to_internal_arc(req_ctx); // NOTE: we must call `Arc::into_raw` at last of the method.

    {
        let mut req_ctx = req_ctx.lock().unwrap();

        if req_ctx.has_body {
            let sender;
            (sender, body) = Body::channel();
            req_ctx.sender = Some(sender);
        } else {
            body = Body::empty();
        }
    }
    {
        let req_ctx = req_ctx.clone();
        runtime_handle.spawn(async move {
            // Prepare for begin request
            let (seq, req) = {
                let mut req_ctx = req_ctx.lock().unwrap();
                assert!(req_ctx.builder.is_some());

                let builder = req_ctx.builder.take().unwrap();
                (req_ctx.seq, builder.body(body).unwrap())
            };

            // Send a request and wait for response status and headers.
            let res = ctx.client.request(req).await;
            if let Err(err) = res {
                LAST_ERROR.with(|v| {
                    *v.borrow_mut() = Some(err.to_string());
                });
                (ctx.on_complete)(seq, 1);
                return;
            }

            // Status code and response headers are received.
            let mut res = res.unwrap();
            {
                let mut req_ctx = req_ctx.lock().unwrap();
                req_ctx.response_headers = Some(
                    res.headers()
                        .iter()
                        .map(|x| {
                            (
                                x.0.to_string(),
                                x.1.to_str().unwrap_or_default().to_string(),
                            )
                        })
                        .collect::<Vec<(String, String)>>(),
                );
                req_ctx.response_status = res.status();
                req_ctx.response_version = YahaHttpVersion::from(res.version());
            }
            (ctx.on_status_code_and_headers_receive)(
                seq,
                res.status().as_u16() as i32,
                YahaHttpVersion::from(res.version()),
            );

            // Read the response body stream.
            let body = res.body_mut();

            while !body.is_end_stream() {
                //println!("body.data().await");
                let received = body.data().await;
                match received {
                    Some(x) => {
                        match x {
                            Ok(y) => {
                                //println!("body.data: on_receive {}; is_end_stream={}", y.len(), body.is_end_stream());
                                (ctx.on_receive)(seq, y.len(), y.as_ptr());
                            }
                            Err(err) => {
                                //println!("body.data: on_complete_error");
                                LAST_ERROR.with(|v| {
                                    *v.borrow_mut() = Some(err.to_string());
                                });
                                (ctx.on_complete)(seq, 1);
                                return;
                            }
                        }
                    }
                    None => {
                        //println!("body.data: None; is_end_stream={}", body.is_end_stream());
                        break;
                    }
                }
            }

            {
                let mut req_ctx = req_ctx.lock().unwrap();
                req_ctx.try_complete();
            }

            //println!("trailers");
            let trailers = res.trailers().await.unwrap_or_default();

            //println!("on_complete");
            match trailers {
                Some(trailers) => {
                    {
                        let mut req_ctx = req_ctx.lock().unwrap();
                        req_ctx.response_trailers = Some(
                            trailers
                                .iter()
                                .map(|x| {
                                    (
                                        x.0.to_string(),
                                        x.1.to_str().unwrap_or_default().to_string(),
                                    )
                                })
                                .collect::<Vec<(String, String)>>(),
                        );
                    }
                    (ctx.on_complete)(seq, 0);
                }
                None => (ctx.on_complete)(seq, 0),
            }

            {
                let mut req_ctx = req_ctx.lock().unwrap();
                req_ctx.completed = true;
            }
        });
    }

    _ = Arc::into_raw(req_ctx);
    true
}

#[no_mangle]
pub extern "C" fn yaha_request_write_body(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    buf: *const u8,
    len: usize,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let slice = unsafe { std::slice::from_raw_parts(buf, len) };
    let result = req_ctx
        .sender
        .as_mut()
        .unwrap()
        .try_send_data(Bytes::from_static(slice));

    result.is_ok()
}

#[no_mangle]
pub extern "C" fn yaha_request_complete_body(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> bool {
    let mut req_ctx = context::to_internal(req_ctx).lock().unwrap();
    //debug_assert!(!req_ctx.completed);

    req_ctx.try_complete();
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_headers_count(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> i32 {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    match req_ctx.response_headers.as_ref() {
        Some(headers) => headers.len() as i32,
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_header_key(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    index: i32,
) -> *const ByteBuffer {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let headers = req_ctx.response_headers.as_ref().unwrap();
    let key_value = headers.get(index as usize).unwrap();
    let buf = ByteBuffer::from_vec(key_value.0.clone().into_bytes());
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_header_value(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    index: i32,
) -> *const ByteBuffer {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let headers = req_ctx.response_headers.as_ref().unwrap();
    let key_value = headers.get(index as usize).unwrap();
    let buf = ByteBuffer::from_vec(key_value.1.clone().into_bytes());
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_trailers_count(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> i32 {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    match req_ctx.response_trailers.as_ref() {
        Some(trailers) => trailers.len() as i32,
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_trailers_key(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    index: i32,
) -> *const ByteBuffer {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let trailers = req_ctx.response_trailers.as_ref().unwrap();
    let key_value = trailers.get(index as usize).unwrap();
    let buf = ByteBuffer::from_vec(key_value.0.clone().into_bytes());
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_trailers_value(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    index: i32,
) -> *const ByteBuffer {
    let req_ctx = context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let trailers = req_ctx.response_trailers.as_ref().unwrap();
    let key_value = trailers.get(index as usize).unwrap();
    let buf = ByteBuffer::from_vec(key_value.1.clone().into_bytes());
    Box::into_raw(Box::new(buf))
}

#[no_mangle]
pub extern "C" fn yaha_request_destroy(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> bool {
    let req_ctx = context::to_internal_arc(req_ctx);
    true
}
