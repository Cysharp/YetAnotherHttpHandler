use std::{
    num::NonZeroIsize,
    sync::{Arc, Mutex},
};
use futures_channel::mpsc::Sender;
use http_body_util::combinators::BoxBody;
use tokio::runtime::{Handle, Runtime};

use hyper::{
    body::Bytes,
    StatusCode,
};

use hyper_util::{
    client::{self, legacy::{connect::HttpConnector, Client}},
    rt::{TokioExecutor, TokioTimer},
};

use hyper_rustls::ConfigBuilderExt;

#[cfg(feature = "rustls")]
use hyper_rustls::HttpsConnector;
#[cfg(feature = "native")]
use hyper_tls::HttpsConnector;
use rustls::pki_types::{CertificateDer, PrivateKeyDer};
use tokio_util::sync::CancellationToken;

use crate::primitives::{YahaHttpVersion, CompletionReason};

type OnStatusCodeAndHeadersReceive =
    extern "C" fn(req_seq: i32, state: NonZeroIsize, status_code: i32, version: YahaHttpVersion);
type OnReceive = extern "C" fn(req_seq: i32, state: NonZeroIsize, length: usize, buf: *const u8);
type OnComplete = extern "C" fn(req_seq: i32, state: NonZeroIsize, reason: CompletionReason, h2_error_code: u32);

pub struct YahaNativeRuntimeContext;
pub struct YahaNativeRuntimeContextInternal {
    pub runtime: Runtime
}

impl YahaNativeRuntimeContextInternal {
    pub fn from_raw_context(ctx: *mut YahaNativeRuntimeContext) -> &'static mut Self {
        unsafe { &mut *(ctx as *mut Self) }
    }
    pub fn new() -> YahaNativeRuntimeContextInternal {
        YahaNativeRuntimeContextInternal {
            runtime: Runtime::new().unwrap()
        }
    }
}

pub struct YahaNativeContext;
pub struct YahaNativeContextInternal<'a> {
    pub runtime: tokio::runtime::Handle,
    pub client_builder: Option<client::legacy::Builder>,
    pub skip_certificate_verification: Option<bool>,
    pub root_certificates: Option<rustls::RootCertStore>,
    pub override_server_name: Option<String>,
    pub client_auth_certificates: Option<Vec<CertificateDer<'a>>>,
    pub client_auth_key: Option<PrivateKeyDer<'a>>,
    pub client: Option<Client<HttpsConnector<HttpConnector>, BoxBody<Bytes, hyper::Error>>>,
    pub on_status_code_and_headers_receive: OnStatusCodeAndHeadersReceive,
    pub on_receive: OnReceive,
    pub on_complete: OnComplete,
}

impl YahaNativeContextInternal<'_> {
    pub fn from_raw_context(ctx: *mut YahaNativeContext) -> &'static mut Self {
        unsafe { &mut *(ctx as *mut Self) }
    }

    pub fn new(
        runtime_handle: Handle,
        on_status_code_and_headers_receive: OnStatusCodeAndHeadersReceive,
        on_receive: OnReceive,
        on_complete: OnComplete,
    ) -> Self {
        YahaNativeContextInternal {
            runtime: runtime_handle,
            client: None,
            client_builder: Some(Client::builder(TokioExecutor::new())),
            skip_certificate_verification: None,
            root_certificates: None,
            override_server_name: None,
            client_auth_certificates: None,
            client_auth_key: None,
            on_status_code_and_headers_receive,
            on_receive,
            on_complete,
        }
    }

    pub fn build_client(&mut self, skip_verify_certificates: bool) {
        let mut builder = self.client_builder.take().unwrap();
        let https = self.new_connector(skip_verify_certificates);
        self.client = Some(builder.timer(TokioTimer::new()).build(https));
    }

    #[cfg(feature = "rustls")]
    fn new_connector(&mut self, skip_verify_certificates: bool) -> HttpsConnector<HttpConnector> {
        let tls_config_builder = rustls::ClientConfig::builder();

        // Configure certificate root store.
        let tls_config: rustls::ClientConfig;
        if skip_verify_certificates {
            tls_config = tls_config_builder
                .dangerous()
                .with_custom_certificate_verifier(Arc::new(danger::NoCertificateVerification {}))
                .with_no_client_auth();
        } else {
            let tls_config_builder_root: rustls::ConfigBuilder<
                rustls::ClientConfig,
                rustls::client::WantsClientCert,
            >;
            if let Some(root_certificates) = &self.root_certificates {
                tls_config_builder_root =
                    tls_config_builder.with_root_certificates(root_certificates.to_owned());
            } else {
                tls_config_builder_root = tls_config_builder.with_webpki_roots();
            }

            tls_config = if let Some(client_auth_certificates) = &self.client_auth_certificates {
                if let Some(client_auth_key) = &self.client_auth_key {
                    let certs: Vec<CertificateDer> = client_auth_certificates
                        .iter()
                        .map(|c| c.clone().into_owned())
                        .collect();

                    tls_config_builder_root
                        .clone()
                        .with_client_auth_cert(
                            certs,
                            client_auth_key.clone_key(),
                        )
                        .unwrap_or(tls_config_builder_root.with_no_client_auth())
                } else {
                    tls_config_builder_root.with_no_client_auth()
                }
            } else {
                tls_config_builder_root.with_no_client_auth()
            }
        }

        let builder = hyper_rustls::HttpsConnectorBuilder::new()
            .with_tls_config(tls_config)
            .https_or_http();

        let builder = if let Some(override_server_name) = &self.override_server_name {
            builder.with_server_name(override_server_name.clone())
        } else {
            builder
        };

        let builder = builder
            .enable_all_versions();

        // Almost the same as `builder.build()`, but specify `set_nodelay(true)`.
        let mut http_conn = HttpConnector::new();
        http_conn.set_nodelay(true);
        http_conn.enforce_http(false);
        builder.wrap_connector(http_conn)
    }

    #[cfg(feature = "native")]
    fn new_connector(&mut self, skip_verify_certificates: bool) -> HttpsConnector<HttpConnector> {
        let https = HttpsConnector::new();
        https
    }
}

#[cfg(feature = "rustls")]
mod danger {
    use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified};
    use rustls::{DigitallySignedStruct, Error, SignatureScheme};
    use rustls::pki_types::{CertificateDer, ServerName, UnixTime};

    #[derive(Debug)]
    pub struct NoCertificateVerification {}

    const ALL_SCHEMES: [SignatureScheme; 12] = [
        SignatureScheme::RSA_PKCS1_SHA1,
        SignatureScheme::ECDSA_SHA1_Legacy,
        SignatureScheme::RSA_PKCS1_SHA256,
        SignatureScheme::ECDSA_NISTP256_SHA256,
        SignatureScheme::RSA_PKCS1_SHA384,
        SignatureScheme::ECDSA_NISTP384_SHA384,
        SignatureScheme::ECDSA_NISTP521_SHA512,
        SignatureScheme::RSA_PSS_SHA256,
        SignatureScheme::RSA_PSS_SHA384,
        SignatureScheme::RSA_PSS_SHA512,
        SignatureScheme::ED25519,
        SignatureScheme::ED448];

    impl rustls::client::danger::ServerCertVerifier for NoCertificateVerification {
        fn verify_server_cert(
            &self,
            _end_entity: &CertificateDer<'_>,
            _intermediates: &[CertificateDer<'_>],
            _server_name: &ServerName<'_>,
            _ocsp_response: &[u8],
            _now: UnixTime) -> Result<ServerCertVerified, Error> {
            Ok(ServerCertVerified::assertion())
        }

        fn verify_tls12_signature(
            &self,
            _message: &[u8],
            _cert: &CertificateDer<'_>,
            _dss: &DigitallySignedStruct) -> Result<HandshakeSignatureValid, Error> {
            Ok(HandshakeSignatureValid::assertion())
        }

        fn verify_tls13_signature(
            &self,
            _message: &[u8],
            _cert: &CertificateDer<'_>,
            _dss: &DigitallySignedStruct) -> Result<HandshakeSignatureValid, Error> {
            Ok(HandshakeSignatureValid::assertion())
        }



        fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
            Vec::from(ALL_SCHEMES)
        }
    }
}

pub struct YahaNativeRequestContext;
pub struct YahaNativeRequestContextInternal {
    pub seq: i32,
    pub builder: Option<hyper::http::request::Builder>,
    pub sender: Option<Sender<Bytes>>,
    pub has_body: bool,
    pub completed: bool,
    pub cancellation_token: CancellationToken,

    pub response_version: YahaHttpVersion,
    pub response_status: StatusCode,
    pub response_headers: Option<Vec<(String, String)>>,
    pub response_trailers: Option<Vec<(String, String)>>,
}

impl YahaNativeRequestContextInternal {
    pub fn try_complete(&mut self) {
        if self.sender.is_some() {
            // By dropping, the sending body channel is completed.
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
