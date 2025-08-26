# YetAnotherHttpHandler
YetAnotherHttpHandler brings the power of HTTP/2 to Unity and .NET Standard.

This library enables the use of HTTP/2, which Unity does not support. It allows you to use grpc-dotnet instead of the deprecated C-core gRPC library. It can also be used for asset downloading via HTTP/2, providing more functionality to your projects.

The library is implemented as a HttpHandler for HttpClient, so you can use the same API by just replacing the handler. (drop-in replacement)

### Highlights
- Unity support (including Apple Silicon support)
- Compatible with gRPC (grpc-dotnet)
- Leveraging `System.Net.Http.HttpClient` API

### Under the hood
The handler is built on top of [hyper](https://hyper.rs/) and [Rustls](https://github.com/rustls/rustls). It is a binding library that brings the power of those libraries to Unity and .NET.

## Supported platforms and runtimes

- Unity 2021.3 (LTS) or later

Architecture/Platform | Windows | Windows (UWP) | macOS | Linux | Android | iOS | iOS (Simulator)
-- | -- | -- | -- | -- | -- | -- | --
<strong>x64 (x86_64)</strong> | ✔ | - | ✔ | ☁ | ☁ | ☁ | -
<strong>arm64 (aarch64, Apple Silicon)</strong> | ☁ | ☁ | ✔ | 📆 | ✔ | ✔ | ☁
<strong>armv7</strong> | - | - | - | - | ☁ | - | -

- ✔ (Tier 1): Verify that it works, and active development support.
- ☁ (Tier 2): The build is available, but not confirmed to work.
- 📆 (Planned): Currently, the build is not available yet.

<!--
- Unity 2021.3 (LTS) or later
	- Editor
		- Windows (x64)
		- macOS (x64, Apple Silicon)
	- Standalone
		- Windows (x64)
		- macOS (x64, Apple Silicon)
		- Linux (x64)
	- Player
		- Windows (x64)
		- macOS (x64, Apple Silicon)
		- Linux (x64)
		- iOS (Arm64, x64)
		- Android (Armv7, Arm64, x64)
-->

## Features
- HTTP/1.0, HTTP/1.1
- HTTP/2
	- Multiple streams on a single connection
	- Compatible with grpc-dotnet (Grpc.Net.Client)
	- HTTP/2 over cleartext
	- TLS 1.2/1.3 with ALPN
		- TLS support is powered by Rustls + webpki
		- Client certificate
    - Unix Domain Socket (macOS, Linux)

### Not supported yet
- HTTP proxy support
- Verification of certificates by security features built into the OS
- More platform supports
	- Linux on Arm

### Not supported (not planned)
- NTLM and Kerberos authentication
- Platforms
	- Unity 2021.2 or earlier
	- .NET 5+
	- 32bit architectures (x86)
	- tvOS, watchOS, Tizen
        - WebGL

## Installation
### Unity

This library depends on the following additional libraries:

- [System.IO.Pipelines](https://www.nuget.org/packages/System.IO.Pipelines) (netstandard2.1)
- [System.Runtime.CompilerServices.Unsafe](https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe) (netstandard2.1)

#### Method 1: Using NuGetForUnity + GitHub

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) to your Unity project
2. Install the following NuGet packages via NuGetForUnity:
    - System.IO.Pipelines
    - System.Runtime.CompilerServices.Unsafe
3. Specify the following URL in `Add package from git URL...` of Package Manager on Unity
```
https://github.com/Cysharp/YetAnotherHttpHandler.git?path=src/YetAnotherHttpHandler#{Version}
```

> [!NOTE]
> Please replace `{Version}` with the version number you want to install (e.g. `1.8.0`). See [Releases](https://github.com/Cysharp/YetAnotherHttpHandler/releases) page.

#### Method 2: Using UnityNuGet scope registry

1. Add the [UnityNuget](https://github.com/xoofx/UnityNuGet) scope registry:

```
https://github.com/xoofx/UnityNuGet#add-scope-registry-manifestjson
```

2. Specify the following URL in `Add package from git URL...` of Package Manager on Unity. This is the package that resolves NuGet dependencies.

```
https://github.com/Cysharp/YetAnotherHttpHandler.git?path=src/YetAnotherHttpHandler.Dependencies#{Version}
```

3. Specify the following URL in `Add package from git URL...` of Package Manager on Unity. This is the main packagee.

```
https://github.com/Cysharp/YetAnotherHttpHandler.git?path=src/YetAnotherHttpHandler#{Version}
```

> [!NOTE]
> Please replace `{Version}` with the version number you want to install (e.g. `1.8.0`). See [Releases](https://github.com/Cysharp/YetAnotherHttpHandler/releases) page.

#### Method 3: Install from GitHub / Git repository

1. Please download and install [Cysharp.Net.Http.YetAnotherHttpHandler.Dependencies.unitypackage
 from the dependency redistribution on the release page](https://github.com/Cysharp/YetAnotherHttpHandler/releases/tag/redist-20240111-01).

2. Specify the following URL in `Add package from git URL...` of Package Manager on Unity.

```
https://github.com/Cysharp/YetAnotherHttpHandler.git?path=src/YetAnotherHttpHandler#{Version}
```

> [!NOTE]
> Please replace `{Version}` with the version number you want to install (e.g. `1.8.0`). See [Releases](https://github.com/Cysharp/YetAnotherHttpHandler/releases) page.

## Usage

Create an instance of YetAnotherHttpHandler and pass it to HttpClient.

```csharp
using Cysharp.Net.Http;

using var handler = new YetAnotherHttpHandler();
var httpClient = new HttpClient(handler);

var result = await httpClient.GetStringAsync("https://www.example.com");
```

With these steps, your HttpClient is now compatible with HTTP/2.✨

`YetAnotherHttpHandler` and `HttpClient` can be held following best practices and shared across multiple threads or requests.

However, since it does not have features such as connection control by the number of streams, it is necessary to separate handler instances when explicitly creating different connections.

### Using gRPC (grpc-dotnet) library

To use grpc-dotnet (Grpc.Net.Client), add the following additional libraries:
 
- Grpc.Core.Api
- Grpc.Net.Client
- Grpc.Net.Common
- Microsoft.Extensions.Logging.Abstractions
- System.Diagnostics.DiagnosticSource

#### Method 1: Using NuGetForUnity

1. Install `Grpc.Net.Client` from NuGet via NuGetForUnity

#### Method 2: Using UnityNuget scope registry

To use grpc-dotnet (Grpc.Net.Client), add the following additional library to `manifest.json`:

```json
{
  "dependencies": {
    "org.nuget.grpc.net.client": "{version}"
  }
}
```

> [!NOTE]
> Replace `{version}` with the latest version available in NuGet: https://www.nuget.org/packages/Grpc.Net.Client#versions-body-tab

#### Method 3: Install pre-built package or install the libraries manually.

Please download and install [Grpc.Net.Client.Dependencies.unitypackage
 from the dependency redistribution on the release page](https://github.com/Cysharp/YetAnotherHttpHandler/releases/tag/redist-20230728-01), or obtain the library from NuGet.


#### Using GrpcChannel with YetAnotherHttpHandler
Create an instance of `YetAnotherHttpHandler` and pass it to `GrpcChannelOptions.HttpHandler` property.

```csharp
using Cysharp.Net.Http;

using var handler = new YetAnotherHttpHandler();
using var channel = GrpcChannel.ForAddress("https://api.example.com", new GrpcChannelOptions() { HttpHandler = handler });
var greeter = new GreeterClient(channel);

var result = await greeter.SayHelloAsync(new HelloRequest { Name = "Alice" });

// -- OR --

using var channel = GrpcChannel.ForAddress("https://api.example.com", new GrpcChannelOptions() { HttpHandler = new YetAnotherHttpHandler(), DisposeHttpClient = true });
var greeter = new GreeterClient(channel);

var result = await greeter.SayHelloAsync(new HelloRequest { Name = "Alice" });
```

## Configurations
When creating a YetAnotherHttpHandler instance, you can configure the following HTTP client settings.

Once the handler sends a request, these settings become immutable and cannot be changed.

|Property| Description |
| -- | -- |
|PoolIdleTimeout|Gets or sets an optional timeout for idle sockets being kept-alive. Default is 90 seconds.|
|MaxIdlePerHost|Gets or sets the maximum idle connection per host allowed in the pool. Default is usize::MAX (no limit).|
|Http2Only|Gets or sets a value that indicates whether to force the use of HTTP/2.|
|SkipCertificateVerification|Gets or sets a value that indicates whether to skip certificate verification.|
|OnVerifyServerCertificate|Gets or sets a custom handler that validates server certificates.|
|RootCertificates|Gets or sets a custom root CA. By default, the built-in root CA (Mozilla's root certificates) is used. See also https://github.com/rustls/webpki-roots. |
|OverrideServerName|Gets or sets a value that specifies subject alternative name (SAN) of the certificate.|
|ClientAuthCertificates|Gets or sets a custom client auth key.|
|ClientAuthKey|Gets or sets a custom client auth certificates.|
|Http2InitialStreamWindowSize|Gets or sets the SETTINGS_INITIAL_WINDOW_SIZE option for HTTP2 stream-level flow control.|
|Http2InitialConnectionWindowSize|Gets or sets the max connection-level flow control for HTTP2|
|Http2AdaptiveWindow|Gets or sets whether to use an adaptive flow control. Enabling this will override the limits set in http2_initial_stream_window_size and http2_initial_connection_window_size.|
|Http2MaxFrameSize|Gets or sets the maximum frame size to use for HTTP2.|
|ConnectTimeout|Gets or sets timeout for TCP connection establishment. Pass null to never timeout. Default is never timeout.|
|Http2KeepAliveInterval|Gets or sets an interval for HTTP2 Ping frames should be sent to keep a connection alive. Pass <value>null</value> to disable HTTP2 keep-alive. Default is currently disabled.|
|Http2KeepAliveTimeout|Gets or sets a timeout for receiving an acknowledgement of the keep-alive ping. If the ping is not acknowledged within the timeout, the connection will be closed. Does nothing if http2_keep_alive_interval is disabled. Default is 20 seconds.|
|Http2KeepAliveWhileIdle|Gets or sets whether HTTP2 keep-alive should apply while the connection is idle. If disabled, keep-alive pings are only sent while there are open request/responses streams. If enabled, pings are also sent when no streams are active. Does nothing if http2_keep_alive_interval is disabled. Default is false.|
|Http2MaxConcurrentResetStreams|Gets or sets the maximum number of HTTP2 concurrent locally reset streams. See the documentation of h2::client::Builder::max_concurrent_reset_streams for more details. The default value is determined by the h2 crate.|
|Http2MaxSendBufferSize|Gets or sets the maximum write buffer size for each HTTP/2 stream. Default is currently 1MB, but may change.|
|Http2InitialMaxSendStreams|Gets or sets the initial maximum of locally initiated (send) streams. This value will be overwritten by the value included in the initial SETTINGS frame received from the peer as part of a connection preface.|
|UnixDomainSocketPath|Gets or sets the path to a Unix Domain Socket to be used as HTTP communication channel instead of the default TCP.|
|ResponsePipeOptions|Gets or sets the options for the pipe used to receive the response body.|

Most of them expose [hyper client settings](https://docs.rs/hyper-util/latest/hyper_util/client/legacy/struct.Builder.html), so please check those as well.

## Advanced
### Using HTTP/2 over cleartext (h2c)
gRPC requires communication over HTTP/2, but when it's a non-HTTPS connection, it defaults to attempting a connection with HTTP/1, causing connection issues.
In this case, setting the `YetAnotherHttpHandle.Http2Only` property to `true` allows for connections via HTTP/2 over cleartext (h2c).

```csharp
using var handler = new YetAnotherHttpHandler() { Http2Only = true };
```

### Using custom root certificates
Currently, YetAnotherHttpHandler uses Mozilla's root certificates derived from webpki as the root CA.

If you want to use a self-signed certificate or a certificate issued by your organization, you need to set a custom root CA. In this case, you can specify the root certificates in pem format to `RootCertificates` property.

```csharp
var rootCerts = @"
-----BEGIN CERTIFICATE-----
MIIE9TCCAt2gAwIBAgIUUQ33LbUPwlgKXmzA77KmDbV2uYkwDQYJKoZIhvcNAQEL
BQAwFDESMBAGA1UEAwwJbG9jYWxob3N0MB4XDTIzMDcyNTAzNDYzNFoXDTMzMDcy
MjAzNDYzNFowFDESMBAGA1UEAwwJbG9jYWxob3N0MIICIjANBgkqhkiG9w0BAQEF
AAOCAg8AMIICCgKCAgEAyuyNn36Sv87u8q7UmB7nhuMe71w6geUstcYKhO5ZahYf
d9I9mGZTKpUvThgm65nrIPT8zE7yRqrgagP+MtuRtwByt9w7lO8Y/lJda4iHaTXd
e9Yq0lZGrv0CeZ7NJZCGfPG9GJHG8Bh4IjjhMwGcNea50vfky72nuZnCdLKLbr55
037bIQ7R2bPfxqNTo0Lcij5ApI6/YlpJZ14vi0yHDSCyTAM9PUlgv6EsYdQ3vf1C
bdg2VlnPiAyYI2f7TRZ3YBrrUU8/qcBSsPoTNYgCaBld35/3JizLZJlWukPWnbe3
TuU9FwRv/Vh+UnD2cnv7p0+JW2coa/9Yrk/W7oSFxGoujg/fKm7O9j76JKD/04U7
yGkizQG4uako3BTcIDgHRsDqyIp9MR2v/nbb8Xol2cHL9nE3+ovrgn9upIFvgZk+
nAuRgAmB4IaBtMS5ih0QJnlLB5FqDj+PkJG+s8iqOphg4V3P07zAvOTk1J96VDLO
lnQHpjwMGXoYaevWHRU+Vmm2rktpTyJVt5xtlqjoN/FBnCYbQpAosS5fciN7ghcs
zCmKVKC0riCa7MwPUooVOa/TqDzv5rGPp2vFXTdKDova7OlTo2YofDd2grOwM5O7
TQp7MHUs1gtnHSEYdMeKWi6fSbtx4Jru13blXV7MMUHaQCg2YpJIqofnXQ5+9FMC
AwEAAaM/MD0wCQYDVR0TBAIwADALBgNVHQ8EBAMCBeAwIwYDVR0RBBwwGoINd3d3
LmxvY2FsaG9zdIIJbG9jYWxob3N0MA0GCSqGSIb3DQEBCwUAA4ICAQAByseWC2Fp
K8S9c/6aHQRwJPgkq1CGMx20wid1XvD9e+F8xF//L5MPa21gx6TC/dRO3rC6Oyqt
mR011vMIh0/DMOhF8eFg9bn8dGSkUiWanw8OKsewTGzJFkoq4cDgO0hjfQTLRNnf
KlDMZLISsnPFSQnhRN7c71j0dXrG+flsazK4CFy9FtXJEePkiCyS5ZSBDkfBKerp
fif6Wz2Zk4yLwmmw0c/sNsgHkRfj3q+Zf1RgpcuUYmYbPigHSI2qpsWbqMeQmIvS
+s7Tap3sQFCYIGCvSmOV4STY2JqxeWOGgR/xLZBpfJgdllfy1mpe7dHpi0kVTEdE
cC1pNeFDn8xYm2M61oGUYy+b035HqD9SfPsnHOFzwgwINuHdL4xjmT+HwAtW+WOj
105d+aIK55gOaJPGa7Pc7UMYtN7Oc/9hWfYti0MXnsyYfCNx6Fl7jtKs1AG2BbQd
sReZj7em23DBe75I4+DCcNWQg40HXsDo2h+z+Xk3SFb/gvHMtmzFudKCDIpD0PS5
gEXEzkKRg/++6iXGF16eBibZ8PED6416rGJz1Bo1YpXSyYCZG6oWwXZTg9fvDZX5
FfLnQACV02y2Gs74h23Yq+QmA30Ly5GPrR5SBRaNiCY1SS7gCAfRah9zJjvbXNGw
h0Eq48Dlw12GaUww3y05/IoAJxtHxZigdQ==
-----END CERTIFICATE-----
";
using var handler = new YetAnotherHttpHandler() { RootCertificates = rootCerts };
```

### Ignore certificate validation errors
We strongly not recommend this, but in some cases, you may want to skip certificate validation when connecting via HTTPS. In this scenario, you can ignore certificate errors by setting the `SkipCertificateVerification` property to `true`.

### Handling server certificate verification
You can customize the server certificate verification process by setting the `OnVerifyServerCertificate` property.

The callback should return `true` or `false` based on the verification result. If the property is set, the root CA verification is not performed.

```csharp
using var httpHandler = new YetAnotherHttpHandler()
{
    OnVerifyServerCertificate = (serverName, certificate, now) =>
    {
        var cert = new X509Certificate2(certificate);
        return serverName == "api.example.com" &&
               cert.Subject == "CN=api.example.com";
    }
};
```

### Using Unix Domain Sockets as HTTP transport layer

Unix Domain Sockets can be used as the HTTP transport layer for local usecases (e.g. IPC based on gRPC), instead of network-based TCP.

Set the `UnixDomainSocketPath` property to enable UDS-based communication to a server listening at the given path:

```csharp
using var handler = new YetAnotherHttpHandler() { Http2Only = true, UnixDomainSocketPath = "/tmp/example.sock" };
using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions() { HttpHandler = handler });
```

Note:
- HTTPS is not supported over UDS. All HTTPS related configuration properties are ignored if UDS is enabled.
- The grpc-dotnet library doesn't handle non-HTTP schemes (like "unix://"), so keep passing an HTTP URI to `GrpcChannel`, e.g. http://localhost.
  The actual HTTP requests will be redirected to `UnixDomainSocketPath` by YetAnotherHttpHandler internally.
- When using Kestrel on the server, you need to set the `KestrelServerOptions.AllowAlternateSchemes` option to `true`.

### Specifying the number of worker threads
YetAnotherHttpHandler's communication processing is run on worker threads of the tokio runtime. The number of worker threads is determined based on the number of CPU cores by default.
You can specify a number of worker threads using the `YetAnotherHttpHandler.SetWorkerThreads` method.

## Troubleshoot
### System.DllNotFoundException occurs when executing a request on Windows

When attempting to execute a request (yaha_init_runtime), the following exception message may be displayed:

> System.DllNotFoundException: Unable to load DLL 'Cysharp.Net.Http.YetAnotherHttpHandler.Native' or one of its dependencies: The specified module could not be found. (0x8007007E)

This may be because the Visual C++ Redistributable Package (vcruntime140.dll) is not installed. [Please download and install it from Microsoft's website](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170).

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

```bash
# Generate THIRD-PARTY-NOTICES using cargo-about
cargo about generate about.hbs > ../THIRD-PARTY-NOTICES
```

## License
MIT License

This library depends on third-party components. Please refer to [THIRD-PARTY-NOTICES](./THIRD-PARTY-NOTICES) for their licenses.
