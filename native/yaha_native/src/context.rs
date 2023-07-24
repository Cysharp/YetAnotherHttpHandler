use std::sync::{Arc, Mutex};

use hyper::{body::Sender, client::{HttpConnector, self}, Client, StatusCode};

#[cfg(feature = "rustls")]
use hyper_rustls::{ConfigBuilderExt, HttpsConnector};
#[cfg(feature = "native")]
use hyper_tls::HttpsConnector;

use crate::primitives::YahaHttpVersion;

type OnStatusCodeAndHeadersReceive =
    extern "C" fn(req_seq: i32, status_code: i32, version: YahaHttpVersion);
type OnReceive = extern "C" fn(req_seq: i32, length: usize, buf: *const u8);
type OnComplete = extern "C" fn(req_seq: i32, has_error: u8);

pub struct YahaNativeContext;
pub struct YahaNativeContextInternal {
    pub runtime: tokio::runtime::Runtime,
    pub client_builder: Option<client::Builder>,
    pub client: Option<Client<HttpsConnector<HttpConnector>, hyper::Body>>,
    pub on_status_code_and_headers_receive: OnStatusCodeAndHeadersReceive,
    pub on_receive: OnReceive,
    pub on_complete: OnComplete,
}

impl YahaNativeContextInternal {
    pub fn from_raw_context(ctx: *mut YahaNativeContext) -> &'static mut Self {
        unsafe { &mut *(ctx as *mut Self) }
    }

    pub fn new(
        on_status_code_and_headers_receive: OnStatusCodeAndHeadersReceive,
        on_receive: OnReceive,
        on_complete: OnComplete,
    ) -> Self {
        YahaNativeContextInternal {
            runtime: tokio::runtime::Runtime::new().unwrap(),
            client: None,
            client_builder: Some(Client::builder()),
            on_status_code_and_headers_receive,
            on_receive,
            on_complete,
        }
    }

    pub fn build_client(&mut self, skip_verify_certificates: bool) {
        let builder = self.client_builder.take().unwrap();

        #[cfg(feature = "rustls")]
        fn new_connector(skip_verify_certificates: bool) -> HttpsConnector<HttpConnector> {
            let mut tls_config = rustls::ClientConfig::builder()
                .with_safe_defaults()
                //.with_native_roots()
                .with_webpki_roots()
                .with_no_client_auth();

            if skip_verify_certificates {
                tls_config
                    .dangerous()
                    .set_certificate_verifier(Arc::new(crate::danger::NoCertificateVerification {}));
            }

            let builder = hyper_rustls::HttpsConnectorBuilder::new()
                .with_tls_config(tls_config)
                .https_or_http()
                .enable_http2();

            builder.build()
        }

        #[cfg(feature = "native")]
        fn new_connector(skip_verify_certificates: bool) -> HttpsConnector<HttpConnector> {
            let https = HttpsConnector::new();
            https
        }

        let https = new_connector(skip_verify_certificates);
        self.client = Some(builder.build(https));
    }
}

pub struct YahaNativeRequestContext;
pub struct YahaNativeRequestContextInternal {
    pub seq: i32,
    pub builder: Option<hyper::http::request::Builder>,
    pub sender: Option<Sender>,
    pub has_body: bool,
    pub completed: bool,

    pub response_version: YahaHttpVersion,
    pub response_status: StatusCode,
    pub response_headers: Option<Vec<(String, String)>>,
    pub response_trailers: Option<Vec<(String, String)>>,
}

impl YahaNativeRequestContextInternal {
    pub fn try_complete(&mut self) {
        if self.sender.is_some() {
            self.sender = None;
        }
    }
}
impl Drop for YahaNativeRequestContextInternal {
    fn drop(&mut self) {
        //println!("YahaNativeRequestContextInternal.Drop");
    }
}

pub trait Internalizable<T> {}

impl Internalizable<Mutex<YahaNativeRequestContextInternal>> for YahaNativeRequestContext {}

pub fn to_internal<'a, T: Internalizable<U>, U>(v: *const T) -> &'a U {
    unsafe { &(*(v as *const U)) }
}
pub fn to_internal_arc<'a, T: Internalizable<U>, U>(v: *const T) -> Arc<U> {
    unsafe { Arc::from_raw(v as *const U) }
}
