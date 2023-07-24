# YetAnotherHttpHandler
YetAnotherHttpHandler that brings the power of HTTP/2 to Unity and .NET Standard.

With this library, you can enable HTTP/2, which is not supported by Unity, and get better performance for asset downloads, as well as use grpc-dotnet based libraries that replace the deprecated gRPC (C-core) library.

The library is implemented as a HttpHandler for HttpClient, so you can use the same API by just replacing the handler. (drop-in replacement)

## Development
### Build & Tests

To debug run in your local environment, you first need to build the native library. Please specify the target as follows when building.

```bash
cargo build --target x86_64-pc-windows-msvc
```

When debugging or running unit tests, the native library is loaded from the following directory.

- native/targets/{arch}-{os}-{toolchain}/{debug,release}/{lib}yaha_native.{dll,so}

When creating a package, The following artifacts directory is used.

- native/artifacts/{.NET RID}/{lib}yaha_native.{dll,so}


## License
MIT License
