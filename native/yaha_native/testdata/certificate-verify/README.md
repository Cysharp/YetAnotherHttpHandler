# CertificateVerify test fixtures

These RSA keys and the self-signed `localhost` certificate are test-only fixtures and are not secrets.

- `key-a.pem` matches `cert-a.pem` and is the positive control.
- `key-b.pem` is intentionally unrelated to `cert-a.pem`. The TLS test server uses this pair to produce an invalid handshake signature.
