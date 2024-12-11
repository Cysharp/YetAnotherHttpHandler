use std::{
    cell::RefCell, error::Error, num::NonZeroIsize, ptr::null, sync::{Arc, Mutex}, time::Duration
};

use http_body_util::{combinators::BoxBody, BodyExt};
use hyper::{
    body::{Body, Bytes, Frame},
    http::{HeaderName, HeaderValue},
    Request, StatusCode, Uri, Version,
};
use rustls::pki_types::{CertificateDer, PrivateKeyDer};
use tokio::select;
use tokio_util::sync::CancellationToken;

use crate::interop::{ByteBuffer, StringBuffer};
use crate::primitives::{CompletionReason, YahaHttpVersion};
use crate::{
    context::{
        YahaNativeContext, YahaNativeContextInternal, YahaNativeRequestContext,
        YahaNativeRequestContextInternal, YahaNativeRuntimeContext,
        YahaNativeRuntimeContextInternal,
    },
    primitives::WriteResult,
};
use futures_util::StreamExt;

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
pub extern "C" fn yaha_init_runtime() -> *mut YahaNativeRuntimeContext {
    let runtime = Box::new(YahaNativeRuntimeContextInternal::new());

    Box::into_raw(runtime) as *mut YahaNativeRuntimeContext
}
#[no_mangle]
pub extern "C" fn yaha_dispose_runtime(ctx: *mut YahaNativeRuntimeContext) {
    let ctx = unsafe { Box::from_raw(ctx as *mut YahaNativeRuntimeContextInternal) };
}

#[no_mangle]
pub extern "C" fn yaha_init_context(
    runtime_ctx: *mut YahaNativeRuntimeContext,
    on_status_code_and_headers_receive: extern "C" fn(
        req_seq: i32,
        state: NonZeroIsize,
        status_code: i32,
        version: YahaHttpVersion,
    ),
    on_receive: extern "C" fn(req_seq: i32, state: NonZeroIsize, length: usize, buf: *const u8),
    on_complete: extern "C" fn(req_seq: i32, state: NonZeroIsize, reason: CompletionReason, h2_error_code: u32),
) -> *mut YahaNativeContext {
    let runtime_ctx = YahaNativeRuntimeContextInternal::from_raw_context(runtime_ctx);
    let ctx = Box::new(YahaNativeContextInternal::new(
        runtime_ctx.runtime.handle().clone(),
        on_status_code_and_headers_receive,
        on_receive,
        on_complete,
    ));
    Box::into_raw(ctx) as *mut YahaNativeContext
}

#[no_mangle]
pub extern "C" fn yaha_dispose_context(ctx: *mut YahaNativeContext) {
    let mut ctx = unsafe { Box::from_raw(ctx as *mut YahaNativeContextInternal) };
    ctx.on_complete = _sentinel_on_complete;
    ctx.on_receive = _sentinel_on_receive;
    ctx.on_status_code_and_headers_receive = _sentinel_on_status_code_and_headers_receive;
}
extern "C" fn _sentinel_on_complete(_: i32, _: NonZeroIsize, _: CompletionReason, _: u32) { panic!("The context has already disposed: on_complete"); }
extern "C" fn _sentinel_on_receive(_: i32, _: NonZeroIsize, _: usize, _: *const u8) { panic!("The context has already disposed: on_receive"); }
extern "C" fn _sentinel_on_status_code_and_headers_receive(_: i32, _: NonZeroIsize, _: i32, _: YahaHttpVersion) { panic!("The context has already disposed: on_status_code_and_headers_receive"); }

#[no_mangle]
pub extern "C" fn yaha_client_config_add_root_certificates(
    ctx: *mut YahaNativeContext,
    root_certs: *const StringBuffer,
) -> usize {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    let root_certificates = ctx
        .root_certificates
        .get_or_insert(rustls::RootCertStore::empty());
    let valid: usize = unsafe {
        rustls_pemfile::certs(&mut (*root_certs).to_bytes())
            .filter_map(Result::ok)
            .map(|cert| root_certificates.add(cert))
            .filter_map(|result| result.is_ok().then(|| 1))
            .sum()
    };

    valid
}

#[no_mangle]
pub extern "C" fn yaha_client_config_add_override_server_name(
    ctx: *mut YahaNativeContext,
    override_server_name: *const StringBuffer,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    let server_name = unsafe { (*override_server_name).to_str() };
    ctx.override_server_name.get_or_insert(server_name.to_string());
}

#[no_mangle]
pub extern "C" fn yaha_client_config_add_client_auth_certificates(
    ctx: *mut YahaNativeContext,
    auth_certs: *const StringBuffer,
) -> usize {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    let certs: Vec<CertificateDer> = unsafe {
        rustls_pemfile::certs(&mut (*auth_certs).to_bytes())
            .filter_map(Result::ok)
            .map(CertificateDer::from)
            .collect()
    };

    let count = certs.len();

    if count > 0 {
        ctx.client_auth_certificates.get_or_insert(certs);
    }

    count
}

#[no_mangle]
pub extern "C" fn yaha_client_config_add_client_auth_key(
    ctx: *mut YahaNativeContext,
    auth_key: *const StringBuffer,
) -> usize {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    let keys: Vec<PrivateKeyDer> = unsafe {
        rustls_pemfile::pkcs8_private_keys(&mut (*auth_key).to_bytes())
            .filter_map(Result::ok)
            .map(PrivateKeyDer::from)
            .collect()
    };

    let count = keys.len();

    if count > 0 {
        ctx.client_auth_key.get_or_insert(keys[0].clone_key());
    }

    count
}

#[no_mangle]
pub extern "C" fn yaha_client_config_skip_certificate_verification(
    ctx: *mut YahaNativeContext,
    val: bool,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.skip_certificate_verification = Some(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_set_server_certificate_verification_handler(
    ctx: *mut YahaNativeContext,
    handler: Option<extern "C" fn(state: NonZeroIsize, server_name: *const u8, server_name_len: usize, certificate_der: *const u8, certificate_der_len: usize, now: u64) -> bool>,
    state: NonZeroIsize
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.server_certificate_verification_handler = handler.map(|x| (x, state));
}

#[no_mangle]
pub extern "C" fn yaha_client_config_pool_idle_timeout(
    ctx: *mut YahaNativeContext,
    val_milliseconds: u64,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .pool_idle_timeout(Duration::from_millis(val_milliseconds));
}

#[no_mangle]
pub extern "C" fn yaha_client_config_pool_max_idle_per_host(
    ctx: *mut YahaNativeContext,
    max_idle: usize,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .pool_max_idle_per_host(max_idle);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_only(ctx: *mut YahaNativeContext, val: bool) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder.as_mut().unwrap().http2_only(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_initial_stream_window_size(
    ctx: *mut YahaNativeContext,
    val: u32,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_initial_stream_window_size(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_initial_connection_window_size(
    ctx: *mut YahaNativeContext,
    val: u32,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_initial_connection_window_size(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_adaptive_window(ctx: *mut YahaNativeContext, val: bool) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_adaptive_window(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_max_frame_size(ctx: *mut YahaNativeContext, val: u32) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_max_frame_size(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_keep_alive_interval(
    ctx: *mut YahaNativeContext,
    interval_milliseconds: u64,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_keep_alive_interval(Duration::from_millis(interval_milliseconds));
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_keep_alive_timeout(
    ctx: *mut YahaNativeContext,
    timeout_milliseconds: u64,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_keep_alive_timeout(Duration::from_millis(timeout_milliseconds));
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_keep_alive_while_idle(
    ctx: *mut YahaNativeContext,
    val: bool,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_keep_alive_while_idle(val);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_connect_timeout(
    ctx: *mut YahaNativeContext,
    timeout_milliseconds: u64,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.connect_timeout.get_or_insert(Duration::from_millis(timeout_milliseconds));
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_max_concurrent_reset_streams(
    ctx: *mut YahaNativeContext,
    max: usize,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_max_concurrent_reset_streams(max.try_into().unwrap());
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_max_send_buf_size(
    ctx: *mut YahaNativeContext,
    max: usize,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_max_send_buf_size(max);
}

#[no_mangle]
pub extern "C" fn yaha_client_config_http2_initial_max_send_streams(
    ctx: *mut YahaNativeContext,
    initial: usize,
) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.client_builder
        .as_mut()
        .unwrap()
        .http2_initial_max_send_streams(initial);
}

#[no_mangle]
pub extern "C" fn yaha_build_client(ctx: *mut YahaNativeContext) {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);
    ctx.build_client();
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
        cancellation_token: CancellationToken::new(),

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
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
    assert!(req_ctx.builder.is_some());

    let builder = req_ctx.builder.take().unwrap();
    match Uri::try_from((*value).to_str()) {
        Ok(uri) => {
            req_ctx.builder = Some(builder.uri(uri));
            true
        }
        Err(err) => {
            LAST_ERROR.with(|v| {
                *v.borrow_mut() = Some(err.to_string());
            });
            false
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_set_version(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    value: YahaHttpVersion,
) -> bool {
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    state: NonZeroIsize
) -> bool {
    let ctx = YahaNativeContextInternal::from_raw_context(ctx);

    // Begin request on async runtime.
    let body;

    let req_ctx = crate::context::to_internal_arc(req_ctx); // NOTE: we must call `Arc::into_raw` at last of the method.

    {
        let mut req_ctx = req_ctx.lock().unwrap();

        let (tx, rx) = futures_channel::mpsc::channel::<Bytes>(0);
        body = BoxBody::new(http_body_util::StreamBody::new(rx.map(|data| Result::Ok(Frame::data(data)))));
        
        if req_ctx.has_body {
            req_ctx.sender = Some(tx);
        } else {
            drop(tx); // close
        }
    }
    {
        let req_ctx = req_ctx.clone();
        ctx.runtime.clone().spawn(async move {
            let cancellation_token = {
                let req_ctx = req_ctx.lock().unwrap();
                req_ctx.cancellation_token.clone()
            };

            // Prepare for begin request
            let (seq, req) = {
                let mut req_ctx = req_ctx.lock().unwrap();
                assert!(req_ctx.builder.is_some());

                let builder = req_ctx.builder.take().unwrap();
                (req_ctx.seq, builder.body(body).unwrap())
            };

            //
            if ctx.client.as_ref().is_none() {
                LAST_ERROR.with(|v| {
                    *v.borrow_mut() = Some("The client has not been built. You need to build it before sending the request. ".to_string());
                });
                (ctx.on_complete)(seq, state, CompletionReason::Error, 0);
                return;
            }

            // Send a request and wait for response status and headers.
            let res = select! {
                _ = cancellation_token.cancelled() => {
                    (ctx.on_complete)(seq, state, CompletionReason::Aborted, 0);
                    return;
                }
                res = ctx.client.as_ref().unwrap().request(req) => {
                    if let Err(err) = res {
                        complete_with_error(ctx, seq, state, err);
                        return;
                    } else {
                        res
                    }
                }
            };

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
                state,
                res.status().as_u16() as i32,
                YahaHttpVersion::from(res.version()),
            );

            // Read the response body stream.
            let body = res.body_mut();

            let mut trailer_received = false;

            while !body.is_end_stream() {
                select! {
                    _ = cancellation_token.cancelled() => {
                        (ctx.on_complete)(seq, state, CompletionReason::Aborted, 0);
                        return;
                    }
                    received = body.frame() => {
                        match received {
                            Some(x) => {
                                match x {
                                    Ok(frame) => {
                                        if frame.is_data() {
                                            let data = frame.into_data().unwrap();
                                            (ctx.on_receive)(seq, state, data.len(), data.as_ptr());
                                        } else if frame.is_trailers() {

                                            trailer_received = true;

                                            {
                                                let mut req_ctx = req_ctx.lock().unwrap();
                                                req_ctx.try_complete();
                                            }

                                            let trailers = frame.into_trailers().unwrap();
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
                                    }
                                    Err(err) => {
                                        //println!("body.data: on_complete_error");
                                        LAST_ERROR.with(|v| {
                                            *v.borrow_mut() = Some(err.to_string());
                                        });

                                        // If the `hyper::Error` has `h2::Error` as inner error, the error has HTTP/2 error code.
                                        let reason = err.source()
                                            .and_then(|e| e.downcast_ref::<h2::Error>())
                                            .and_then(|e| e.reason());

                                        let rc = reason.map(|r| u32::from(r));

                                        (ctx.on_complete)(seq, state, CompletionReason::Error, rc.unwrap_or_default());
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
                }
            }

            if !trailer_received {
                let mut req_ctx = req_ctx.lock().unwrap();
                req_ctx.try_complete();
            }

            (ctx.on_complete)(seq, state, CompletionReason::Success, 0);

            {
                let mut req_ctx = req_ctx.lock().unwrap();
                req_ctx.completed = true;
            }
        });
    }

    _ = Arc::into_raw(req_ctx);
    true
}

fn complete_with_error(ctx: &mut YahaNativeContextInternal, seq: i32, state: NonZeroIsize, err: hyper_util::client::legacy::Error) {
    let mut h2_error_code = None;

    // If the error has the inner error, use its error message instead.
    if let Some(error_inner) = err.source() {
        LAST_ERROR.with(|v| {
            *v.borrow_mut() = Some(format!("{}: {}", err.to_string(), error_inner.to_string()));
        });
        
        // If the Error has `h2::Error` as inner error, the error has HTTP/2 error code.
        h2_error_code = error_inner.source()
            .and_then(|e| e.downcast_ref::<h2::Error>())
            .and_then(|e| e.reason())
            .map(|e| u32::from(e));
    } else {
        LAST_ERROR.with(|v| {
            *v.borrow_mut() = Some(err.to_string());
        });
    }

    (ctx.on_complete)(seq, state, CompletionReason::Error, h2_error_code.unwrap_or_default());
}

#[no_mangle]
pub extern "C" fn yaha_request_abort(ctx: *const YahaNativeContext, req_ctx: *const YahaNativeRequestContext) {
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
    req_ctx.cancellation_token.cancel()
}

#[no_mangle]
pub extern "C" fn yaha_request_write_body(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
    buf: *const u8,
    len: usize,
) -> WriteResult {
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
    debug_assert!(!req_ctx.completed);

    let slice = unsafe { std::slice::from_raw_parts(buf, len) };

    match req_ctx.sender.as_mut() {
        Some(sender) => {
            let result = sender.try_send(Bytes::copy_from_slice(slice));
            match result {
                Ok(_) => WriteResult::Success,
                Err(_) => WriteResult::Full,
            }
        }

        // The request has been completed.
        None => WriteResult::AlreadyCompleted
    }
}

#[no_mangle]
pub extern "C" fn yaha_request_complete_body(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> bool {
    let mut req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
    //debug_assert!(!req_ctx.completed);

    req_ctx.try_complete();
    true
}

#[no_mangle]
pub unsafe extern "C" fn yaha_request_response_get_headers_count(
    ctx: *const YahaNativeContext,
    req_ctx: *const YahaNativeRequestContext,
) -> i32 {
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal(req_ctx).lock().unwrap();
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
    let req_ctx = crate::context::to_internal_arc(req_ctx);
    true
}
