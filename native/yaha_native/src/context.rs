use std::{
    num::NonZeroIsize,
    sync::{Arc, Mutex},
    time::Duration,
};
use futures_channel::mpsc::Sender;
use http_body_util::combinators::BoxBody;
use tokio::runtime::{Builder, Handle, Runtime};

use hyper::{
    body::Bytes, 
    Request, StatusCode
};

use hyper_util::{
    client::{self, legacy::{connect::HttpConnector, Client, ResponseFuture}},
    rt::{TokioExecutor, TokioTimer},
};

use hyper_rustls::ConfigBuilderExt;

#[cfg(feature = "rustls")]
use hyper_rustls::HttpsConnector;
#[cfg(feature = "native")]
use hyper_tls::HttpsConnector;
#[cfg(unix)]
use hyperlocal::UnixConnector;

use rustls::pki_types::{CertificateDer, PrivateKeyDer};
use tokio_util::sync::CancellationToken;

use crate::callback_gate::CallbackGate;

use crate::{primitives::{CompletionReason, YahaHttpVersion}};

type OnStatusCodeAndHeadersReceive =
    extern "C" fn(req_seq: i32, state: NonZeroIsize, status_code: i32, version: YahaHttpVersion);
type OnReceive = extern "C" fn(req_seq: i32, state: NonZeroIsize, length: usize, buf: *const u8, task_handle: usize);
type OnComplete = extern "C" fn(req_seq: i32, state: NonZeroIsize, reason: CompletionReason, h2_error_code: u32);
type OnServerCertificateVerificationHandler = extern "C" fn(callback_state: NonZeroIsize, server_name: *const u8, server_name_len: usize, certificate_der: *const u8, certificate_der_len: usize, now: u64) -> bool;

pub struct YahaNativeRuntimeContext;
pub struct YahaNativeRuntimeContextInternal {
    pub runtime: Runtime
}

impl YahaNativeRuntimeContextInternal {
    pub fn from_raw_context(ctx: *mut YahaNativeRuntimeContext) -> &'static mut Self {
        unsafe { &mut *(ctx as *mut Self) }
    }
    pub fn new(worker_threads: i32) -> YahaNativeRuntimeContextInternal {
        let mut builder = Builder::new_multi_thread();
        let mut builder = builder.enable_all();

        // Set the number of worker threads. If not larger than 0, use the default number of threads. (= number of cores)
        if worker_threads > 0 {
            builder = builder.worker_threads(worker_threads as usize);
        }

        YahaNativeRuntimeContextInternal {
            runtime: builder.build().unwrap(),
        }
    }
}

pub struct YahaNativeContext;
pub struct YahaNativeContextInternal<'a> {
    pub runtime: tokio::runtime::Handle,
    pub callbacks: CallbackGate,
    pub client_builder: Option<client::legacy::Builder>,
    pub skip_certificate_verification: Option<bool>,
    pub server_certificate_verification_handler: Option<(OnServerCertificateVerificationHandler, NonZeroIsize)>,
    pub root_certificates: Option<rustls::RootCertStore>,
    pub override_server_name: Option<String>,
    pub connect_timeout: Option<Duration>,
    pub client_auth_certificates: Option<Vec<CertificateDer<'a>>>,
    pub client_auth_key: Option<PrivateKeyDer<'a>>,
    pub tcp_client: Option<Client<HttpsConnector<HttpConnector>, BoxBody<Bytes, hyper::Error>>>,
    pub on_status_code_and_headers_receive: OnStatusCodeAndHeadersReceive,
    pub on_receive: OnReceive,
    pub on_complete: OnComplete,

    #[cfg(unix)]
    pub uds_client: Option<Client<UnixConnector, BoxBody<Bytes, hyper::Error>>>,
    #[cfg(unix)]
    pub uds_socket_path: Option<std::path::PathBuf>,
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
            callbacks: CallbackGate::default(),
            tcp_client: None,
            client_builder: Some(Client::builder(TokioExecutor::new())),
            skip_certificate_verification: None,
            server_certificate_verification_handler: None,
            root_certificates: None,
            override_server_name: None,
            connect_timeout: None,
            client_auth_certificates: None,
            client_auth_key: None,
            on_status_code_and_headers_receive,
            on_receive,
            on_complete,
            #[cfg(unix)]
            uds_client: None,
            #[cfg(unix)]
            uds_socket_path: None,
        }
    }

    pub fn notify_headers(&self, seq: i32, state: NonZeroIsize, status: i32, version: YahaHttpVersion) {
        if let Some(_guard) = self.callbacks.enter() {
            (self.on_status_code_and_headers_receive)(seq, state, status, version);
        }
    }

    pub fn notify_complete(&self, seq: i32, state: NonZeroIsize, reason: CompletionReason, h2_error_code: u32) {
        if let Some(_guard) = self.callbacks.enter() {
            (self.on_complete)(seq, state, reason, h2_error_code);
        }
    }

    pub fn build_client(&mut self) {
        let mut builder = self.client_builder.take().unwrap();
        builder.timer(TokioTimer::new());

        #[cfg(unix)]
        {
            if self.uds_socket_path.is_some() {
                self.uds_client = Some(builder.build(UnixConnector));
            } else {
                let https = self.new_connector();
                self.tcp_client = Some(builder.build(https));
            }
        }
        #[cfg(not(unix))]
        {
            let https = self.new_connector();
            self.tcp_client = Some(builder.build(https));
        }
    }

    #[cfg(feature = "rustls")]
    fn new_connector(&mut self) -> HttpsConnector<HttpConnector> {
        let tls_config_builder = rustls::ClientConfig::builder();

        // Configure certificate root store.
        let tls_config: rustls::ClientConfig;
        if let Some(server_certificate_verification_handler) = self.server_certificate_verification_handler {
            // Use custom certificate verification handler
            let signature_algorithms =
                rustls::crypto::ring::default_provider().signature_verification_algorithms;
            tls_config = tls_config_builder
                .dangerous()
                .with_custom_certificate_verifier(Arc::new(
                    danger::CustomCerficateVerification::new(
                        server_certificate_verification_handler,
                        signature_algorithms,
                        self.callbacks.clone(),
                    ),
                ))
                .with_no_client_auth();
        } else if self.skip_certificate_verification.unwrap_or_default() {
            // Skip certificate verification
            tls_config = tls_config_builder
                .dangerous()
                .with_custom_certificate_verifier(Arc::new(danger::NoCertificateVerification{}))
                .with_no_client_auth();
        } else {
            // Configure to use built-in certification store and client authentication.
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
        http_conn.set_connect_timeout(self.connect_timeout);
        builder.wrap_connector(http_conn)
    }

    #[cfg(feature = "native")]
    fn new_connector(&mut self, server_certificate_verification_handler: Option<OnServerCertificateVerificationHandler>) -> HttpsConnector<HttpConnector> {
        let https = HttpsConnector::new();
        https
    }

    #[cfg(unix)]
    pub fn request(&self, mut req: Request<BoxBody<Bytes, hyper::Error>>) -> ResponseFuture {
        // Precondition (`uds_client` or `tcp_client` is set) ensured by `Self::build_client` and `yaha_request_begin`
        if let Some(uds_socket_path) = &self.uds_socket_path {
            // Transform HTTP URIs to the format expected by hyperlocal
            let path_and_query = req
                .uri()
                .path_and_query()
                .map(|pq| pq.as_str())
                .unwrap_or("/");
            let uds_uri = hyperlocal::Uri::new(uds_socket_path, path_and_query);
            *req.uri_mut() = uds_uri.into();

            self.uds_client.as_ref().unwrap().request(req)
        } else {
            self.tcp_client.as_ref().unwrap().request(req)
        }
    }
    #[cfg(not(unix))]
    pub fn request(&self, req: Request<BoxBody<Bytes, hyper::Error>>) -> ResponseFuture {
        self.tcp_client.as_ref().unwrap().request(req)
    }
}

#[cfg(feature = "rustls")]
mod danger {
    use std::{fmt, num::NonZeroIsize};

    use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified};
    use rustls::crypto::WebPkiSupportedAlgorithms;
    use rustls::{DigitallySignedStruct, Error, SignatureScheme};
    use rustls::pki_types::{CertificateDer, ServerName, UnixTime};

    use super::OnServerCertificateVerificationHandler;
    use crate::callback_gate::CallbackGate;

    #[derive(Debug)]
    pub struct NoCertificateVerification {}

    pub struct CustomCerficateVerification {
        handler: (OnServerCertificateVerificationHandler, NonZeroIsize),
        signature_algorithms: WebPkiSupportedAlgorithms,
        callbacks: CallbackGate,
    }

    impl CustomCerficateVerification {
        pub fn new(
            handler: (OnServerCertificateVerificationHandler, NonZeroIsize),
            signature_algorithms: WebPkiSupportedAlgorithms,
            callbacks: CallbackGate,
        ) -> Self {
            Self {
                handler,
                signature_algorithms,
                callbacks,
            }
        }
    }

    impl fmt::Debug for CustomCerficateVerification {
        fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
            formatter
                .debug_struct("CustomCerficateVerification")
                .finish_non_exhaustive()
        }
    }

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

    impl rustls::client::danger::ServerCertVerifier for CustomCerficateVerification {
        fn verify_server_cert(
            &self,
            end_entity: &CertificateDer<'_>,
            _intermediates: &[CertificateDer<'_>],
            server_name: &ServerName<'_>,
            _ocsp_response: &[u8],
            now: UnixTime,
        ) -> Result<ServerCertVerified, Error> {
            let Some(_guard) = self.callbacks.enter() else {
                return Err(Error::InvalidCertificate(rustls::CertificateError::ApplicationVerificationFailure));
            };
            let server_name = server_name.to_str();
            let server_name = server_name.as_bytes();
            let cetificate_der = end_entity.as_ref();

            if (self.handler.0)(self.handler.1, server_name.as_ptr(), server_name.len(), cetificate_der.as_ptr(), cetificate_der.len(), now.as_secs()) {
                Ok(ServerCertVerified::assertion())
            } else {
                Err(Error::InvalidCertificate(rustls::CertificateError::ApplicationVerificationFailure))
            }
        }

        fn verify_tls12_signature(
            &self,
            message: &[u8],
            cert: &CertificateDer<'_>,
            dss: &DigitallySignedStruct,
        ) -> Result<HandshakeSignatureValid, Error> {
            rustls::crypto::verify_tls12_signature(
                message,
                cert,
                dss,
                &self.signature_algorithms,
            )
        }

        fn verify_tls13_signature(
            &self,
            message: &[u8],
            cert: &CertificateDer<'_>,
            dss: &DigitallySignedStruct,
        ) -> Result<HandshakeSignatureValid, Error> {
            rustls::crypto::verify_tls13_signature(
                message,
                cert,
                dss,
                &self.signature_algorithms,
            )
        }

        fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
            self.signature_algorithms.supported_schemes()
        }
    }

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
    pub last_error: Option<String>,

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

#[cfg(all(test, feature = "rustls"))]
mod tests {
    use std::{
        io::{self, Cursor},
        net::{TcpListener, TcpStream},
        num::NonZeroIsize,
        sync::{
            atomic::{AtomicUsize, Ordering},
            Arc,
        },
        thread,
        time::Duration,
    };

    use rustls::{
        client::danger::ServerCertVerifier,
        pki_types::{CertificateDer, PrivateKeyDer, ServerName, UnixTime},
        ClientConfig, ClientConnection, ServerConfig, ServerConnection, SupportedProtocolVersion,
    };

    use super::danger::CustomCerficateVerification;
    use crate::callback_gate::CallbackGate;

    const CERTIFICATE_A: &[u8] = include_bytes!("../testdata/certificate-verify/cert-a.pem");
    const PRIVATE_KEY_A: &[u8] = include_bytes!("../testdata/certificate-verify/key-a.pem");
    const PRIVATE_KEY_B: &[u8] = include_bytes!("../testdata/certificate-verify/key-b.pem");

    extern "C" fn accept_server_certificate(
        _state: NonZeroIsize,
        _server_name: *const u8,
        _server_name_len: usize,
        _certificate_der: *const u8,
        _certificate_der_len: usize,
        _now: u64,
    ) -> bool {
        true
    }

    fn certificate_chain() -> Vec<CertificateDer<'static>> {
        rustls_pemfile::certs(&mut Cursor::new(CERTIFICATE_A))
            .collect::<Result<Vec<_>, _>>()
            .expect("the test certificate must be valid PEM")
    }

    fn private_key(pem: &'static [u8]) -> PrivateKeyDer<'static> {
        rustls_pemfile::private_key(&mut Cursor::new(pem))
            .expect("the test private key must be valid PEM")
            .expect("the test private key must be present")
    }

    fn complete_handshake(
        protocol_version: &'static SupportedProtocolVersion,
        server_private_key: &'static [u8],
    ) -> io::Result<(usize, usize)> {
        // rustls 0.23+ rejects mismatched certificate/key pairs here. When upgrading from 0.22,
        // use a custom certificate resolver so the negative tests still reach the handshake.
        let server_config = ServerConfig::builder_with_protocol_versions(&[protocol_version])
            .with_no_client_auth()
            // Supplying PRIVATE_KEY_B intentionally creates a server that presents
            // CERTIFICATE_A but signs the TLS handshake with an unrelated RSA key.
            .with_single_cert(certificate_chain(), private_key(server_private_key))
            .expect("the test server key must be supported");

        let listener =
            TcpListener::bind("127.0.0.1:0").expect("the test server must bind to a loopback port");
        let server_address = listener
            .local_addr()
            .expect("the test server must have a local address");

        let server = thread::spawn(move || {
            let (mut socket, _) = listener
                .accept()
                .expect("the test server must accept the client connection");
            socket
                .set_read_timeout(Some(Duration::from_secs(5)))
                .expect("the server read timeout must be configurable");
            socket
                .set_write_timeout(Some(Duration::from_secs(5)))
                .expect("the server write timeout must be configurable");

            let mut connection = ServerConnection::new(Arc::new(server_config))
                .expect("the test server connection must be created");
            // An error is expected when the client correctly rejects PRIVATE_KEY_B.
            let _ = connection.complete_io(&mut socket);
        });

        let verifier = CustomCerficateVerification::new(
            (
                accept_server_certificate,
                NonZeroIsize::new(1).expect("the callback state must be non-zero"),
            ),
            rustls::crypto::ring::default_provider().signature_verification_algorithms,
            CallbackGate::default(),
        );
        let client_config = ClientConfig::builder_with_protocol_versions(&[protocol_version])
            .dangerous()
            .with_custom_certificate_verifier(Arc::new(verifier))
            .with_no_client_auth();

        let mut socket = TcpStream::connect(server_address)
            .expect("the test client must connect to the loopback server");
        socket
            .set_read_timeout(Some(Duration::from_secs(5)))
            .expect("the client read timeout must be configurable");
        socket
            .set_write_timeout(Some(Duration::from_secs(5)))
            .expect("the client write timeout must be configurable");

        let server_name =
            ServerName::try_from("localhost").expect("localhost must be a valid DNS name");
        let mut connection = ClientConnection::new(Arc::new(client_config), server_name)
            .expect("the test client connection must be created");

        let result = connection.complete_io(&mut socket);
        drop(socket);
        server
            .join()
            .expect("the test server thread must not panic");
        result
    }

    #[test]
    fn custom_verifier_accepts_matching_tls12_handshake_signature() {
        complete_handshake(&rustls::version::TLS12, PRIVATE_KEY_A)
            .expect("a TLS 1.2 signature made by the certificate key must be accepted");
    }

    #[test]
    fn custom_verifier_rejects_mismatched_tls12_handshake_signature() {
        complete_handshake(&rustls::version::TLS12, PRIVATE_KEY_B)
            .expect_err("a TLS 1.2 signature made by an unrelated key must be rejected");
    }

    #[test]
    fn custom_verifier_accepts_matching_tls13_handshake_signature() {
        complete_handshake(&rustls::version::TLS13, PRIVATE_KEY_A)
            .expect("a TLS 1.3 signature made by the certificate key must be accepted");
    }

    #[test]
    fn custom_verifier_rejects_mismatched_tls13_handshake_signature() {
        complete_handshake(&rustls::version::TLS13, PRIVATE_KEY_B)
            .expect_err("a TLS 1.3 signature made by an unrelated key must be rejected");
    }

    extern "C" fn verify(state: NonZeroIsize, _: *const u8, _: usize, _: *const u8, _: usize, _: u64) -> bool {
        unsafe { &*(state.get() as *const AtomicUsize) }.fetch_add(1, Ordering::SeqCst);
        true
    }

    #[test]
    fn previously_created_tls_verifier_observes_closed_gate() {
        let calls = AtomicUsize::new(0);
        let callbacks = CallbackGate::default();
        let verifier = CustomCerficateVerification::new(
            (verify, NonZeroIsize::new(&calls as *const _ as isize).unwrap()),
            rustls::crypto::ring::default_provider().signature_verification_algorithms,
            callbacks.clone(),
        );
        let certificate = CertificateDer::from(&b"test certificate"[..]);
        let name = ServerName::try_from("localhost").unwrap();
        let now = UnixTime::since_unix_epoch(Duration::from_secs(0));
        assert!(verifier.verify_server_cert(&certificate, &[], &name, &[], now).is_ok());
        callbacks.close();
        callbacks.wait();
        assert!(verifier.verify_server_cert(&certificate, &[], &name, &[], now).is_err());
        assert_eq!(calls.load(Ordering::SeqCst), 1);
    }
}
