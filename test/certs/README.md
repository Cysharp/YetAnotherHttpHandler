# Certificates for tests

- localhost.{crt,key,pfx}: Server certifiacte (localhost)
- client.{crt,key,pfx}: Client cerfiticate (CN=client.example.com)
- client_unknown.{crt,key,pfx}: Client cerfiticate (CN=unknown.example.com)

## To generate client certificates

```bash
openssl genpkey -algorithm ec -pkeyopt ec_paramgen_curve:P-256 -out client.key
openssl req -key client.key -config client.conf -new -out client.csr
openssl x509 -req -in client.csr -CA localhost.crt -CAkey localhost.key -days 3650 -CAcreateserial -out client.crt
openssl pkcs12 -export -in client.crt -inkey client.key -out client.pfx
```