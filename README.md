# YetAnotherHttpHandler
YetAnotherHttpHandler that brings the power of HTTP/2 to Unity and .NET Standard.

With this library, you can enable HTTP/2, which is not supported by Unity, and get better performance for asset downloads, as well as use grpc-dotnet based libraries that replace the deprecated gRPC (C-core) library.

The library is implemented as a HttpHandler for HttpClient, so you can use the same API by just replacing the handler. (drop-in replacement)

## License
MIT License
