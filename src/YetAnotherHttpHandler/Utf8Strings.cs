using System;
using System.Net.Http;

namespace Cysharp.Net.Http
{
    internal static class Utf8Strings
    {
        public static class HttpMethods
        {
            public static ReadOnlySpan<byte> Get => new byte[] { 0x47, 0x45, 0x54 };
            public static ReadOnlySpan<byte> Put => new byte[] { 0x50, 0x55, 0x54 };
            public static ReadOnlySpan<byte> Post => new byte[] { 0x50, 0x4f, 0x53, 0x54 };
            public static ReadOnlySpan<byte> Delete => new byte[] { 0x44, 0x45, 0x4c, 0x45, 0x54, 0x45 };
            public static ReadOnlySpan<byte> Head => new byte[] { 0x48, 0x45, 0x41, 0x44 };
            public static ReadOnlySpan<byte> Options => new byte[] { 0x4f, 0x50, 0x54, 0x49, 0x4f, 0x4e, 0x53 };
            public static ReadOnlySpan<byte> Trace => new byte[] { 0x54, 0x52, 0x41, 0x43, 0x45 };
            public static ReadOnlySpan<byte> Patch => new byte[] { 0x50, 0x41, 0x54, 0x43, 0x48 };
            public static ReadOnlySpan<byte> Connect => new byte[] { 0x43, 0x4f, 0x4e, 0x4e, 0x45, 0x43, 0x54 };

            public static TempUtf8String FromHttpMethod(HttpMethod method)
            {
                if (HttpMethod.Get == method) { return new TempUtf8String(Get); }
                if (HttpMethod.Put == method) { return new TempUtf8String(Put); }
                if (HttpMethod.Post == method) { return new TempUtf8String(Post); }
                if (HttpMethod.Delete == method) { return new TempUtf8String(Delete); }
                if (HttpMethod.Head == method) { return new TempUtf8String(Head); }
                if (HttpMethod.Options == method) { return new TempUtf8String(Options); }
                if (HttpMethod.Trace == method) { return new TempUtf8String(Trace); }
#if !NETSTANDARD2_0
                if (HttpMethod.Patch == method) { return new TempUtf8String(Patch); }
#endif
#if NET6_0_OR_GREATER
                if (HttpMethod.Connect == method) { return new TempUtf8String(Connect); }
#endif
                return new TempUtf8String(method.Method);
            }
        }

        // https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/Headers/KnownHeaders.cs
        public static class HttpHeaders
        {
            public static ReadOnlySpan<byte> Accept => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74 };
            public static ReadOnlySpan<byte> AcceptCharset => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74, 0x2d, 0x43, 0x68, 0x61, 0x72, 0x73, 0x65, 0x74 };
            public static ReadOnlySpan<byte> AcceptEncoding => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74, 0x2d, 0x45, 0x6e, 0x63, 0x6f, 0x64, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> AcceptLanguage => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74, 0x2d, 0x4c, 0x61, 0x6e, 0x67, 0x75, 0x61, 0x67, 0x65 };
            public static ReadOnlySpan<byte> AcceptPatch => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74, 0x2d, 0x50, 0x61, 0x74, 0x63, 0x68 };
            public static ReadOnlySpan<byte> AcceptRanges => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x70, 0x74, 0x2d, 0x52, 0x61, 0x6e, 0x67, 0x65, 0x73 };
            public static ReadOnlySpan<byte> AccessControlAllowCredentials => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x41, 0x6c, 0x6c, 0x6f, 0x77, 0x2d, 0x43, 0x72, 0x65, 0x64, 0x65, 0x6e, 0x74, 0x69, 0x61, 0x6c, 0x73 };
            public static ReadOnlySpan<byte> AccessControlAllowHeaders => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x41, 0x6c, 0x6c, 0x6f, 0x77, 0x2d, 0x48, 0x65, 0x61, 0x64, 0x65, 0x72, 0x73 };
            public static ReadOnlySpan<byte> AccessControlAllowMethods => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x41, 0x6c, 0x6c, 0x6f, 0x77, 0x2d, 0x4d, 0x65, 0x74, 0x68, 0x6f, 0x64, 0x73 };
            public static ReadOnlySpan<byte> AccessControlAllowOrigin => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x41, 0x6c, 0x6c, 0x6f, 0x77, 0x2d, 0x4f, 0x72, 0x69, 0x67, 0x69, 0x6e };
            public static ReadOnlySpan<byte> AccessControlExposeHeaders => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x45, 0x78, 0x70, 0x6f, 0x73, 0x65, 0x2d, 0x48, 0x65, 0x61, 0x64, 0x65, 0x72, 0x73 };
            public static ReadOnlySpan<byte> AccessControlMaxAge => new byte[] { 0x41, 0x63, 0x63, 0x65, 0x73, 0x73, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c, 0x2d, 0x4d, 0x61, 0x78, 0x2d, 0x41, 0x67, 0x65 };
            public static ReadOnlySpan<byte> Age => new byte[] { 0x41, 0x67, 0x65 };
            public static ReadOnlySpan<byte> Allow => new byte[] { 0x41, 0x6c, 0x6c, 0x6f, 0x77 };
            public static ReadOnlySpan<byte> AltSvc => new byte[] { 0x41, 0x6c, 0x74, 0x2d, 0x53, 0x76, 0x63 };
            public static ReadOnlySpan<byte> AltUsed => new byte[] { 0x41, 0x6c, 0x74, 0x2d, 0x55, 0x73, 0x65, 0x64 };
            public static ReadOnlySpan<byte> Authorization => new byte[] { 0x41, 0x75, 0x74, 0x68, 0x6f, 0x72, 0x69, 0x7a, 0x61, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> CacheControl => new byte[] { 0x43, 0x61, 0x63, 0x68, 0x65, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x72, 0x6f, 0x6c };
            public static ReadOnlySpan<byte> Connection => new byte[] { 0x43, 0x6f, 0x6e, 0x6e, 0x65, 0x63, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> ContentDisposition => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x44, 0x69, 0x73, 0x70, 0x6f, 0x73, 0x69, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> ContentEncoding => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x45, 0x6e, 0x63, 0x6f, 0x64, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> ContentLanguage => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x4c, 0x61, 0x6e, 0x67, 0x75, 0x61, 0x67, 0x65 };
            public static ReadOnlySpan<byte> ContentLength => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x4c, 0x65, 0x6e, 0x67, 0x74, 0x68 };
            public static ReadOnlySpan<byte> ContentLocation => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x4c, 0x6f, 0x63, 0x61, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> ContentMD5 => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x4d, 0x44, 0x35 };
            public static ReadOnlySpan<byte> ContentRange => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x52, 0x61, 0x6e, 0x67, 0x65 };
            public static ReadOnlySpan<byte> ContentSecurityPolicy => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x53, 0x65, 0x63, 0x75, 0x72, 0x69, 0x74, 0x79, 0x2d, 0x50, 0x6f, 0x6c, 0x69, 0x63, 0x79 };
            public static ReadOnlySpan<byte> ContentType => new byte[] { 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x54, 0x79, 0x70, 0x65 };
            public static ReadOnlySpan<byte> Cookie => new byte[] { 0x43, 0x6f, 0x6f, 0x6b, 0x69, 0x65 };
            public static ReadOnlySpan<byte> Cookie2 => new byte[] { 0x43, 0x6f, 0x6f, 0x6b, 0x69, 0x65, 0x32 };
            public static ReadOnlySpan<byte> Date => new byte[] { 0x44, 0x61, 0x74, 0x65 };
            public static ReadOnlySpan<byte> ETag => new byte[] { 0x45, 0x54, 0x61, 0x67 };
            public static ReadOnlySpan<byte> Expect => new byte[] { 0x45, 0x78, 0x70, 0x65, 0x63, 0x74 };
            public static ReadOnlySpan<byte> ExpectCT => new byte[] { 0x45, 0x78, 0x70, 0x65, 0x63, 0x74, 0x2d, 0x43, 0x54 };
            public static ReadOnlySpan<byte> Expires => new byte[] { 0x45, 0x78, 0x70, 0x69, 0x72, 0x65, 0x73 };
            public static ReadOnlySpan<byte> From => new byte[] { 0x46, 0x72, 0x6f, 0x6d };
            public static ReadOnlySpan<byte> GrpcEncoding => new byte[] { 0x67, 0x72, 0x70, 0x63, 0x2d, 0x65, 0x6e, 0x63, 0x6f, 0x64, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> GrpcMessage => new byte[] { 0x67, 0x72, 0x70, 0x63, 0x2d, 0x6d, 0x65, 0x73, 0x73, 0x61, 0x67, 0x65 };
            public static ReadOnlySpan<byte> GrpcStatus => new byte[] { 0x67, 0x72, 0x70, 0x63, 0x2d, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73 };
            public static ReadOnlySpan<byte> Host => new byte[] { 0x48, 0x6f, 0x73, 0x74 };
            public static ReadOnlySpan<byte> IfMatch => new byte[] { 0x49, 0x66, 0x2d, 0x4d, 0x61, 0x74, 0x63, 0x68 };
            public static ReadOnlySpan<byte> IfModifiedSince => new byte[] { 0x49, 0x66, 0x2d, 0x4d, 0x6f, 0x64, 0x69, 0x66, 0x69, 0x65, 0x64, 0x2d, 0x53, 0x69, 0x6e, 0x63, 0x65 };
            public static ReadOnlySpan<byte> IfNoneMatch => new byte[] { 0x49, 0x66, 0x2d, 0x4e, 0x6f, 0x6e, 0x65, 0x2d, 0x4d, 0x61, 0x74, 0x63, 0x68 };
            public static ReadOnlySpan<byte> IfRange => new byte[] { 0x49, 0x66, 0x2d, 0x52, 0x61, 0x6e, 0x67, 0x65 };
            public static ReadOnlySpan<byte> IfUnmodifiedSince => new byte[] { 0x49, 0x66, 0x2d, 0x55, 0x6e, 0x6d, 0x6f, 0x64, 0x69, 0x66, 0x69, 0x65, 0x64, 0x2d, 0x53, 0x69, 0x6e, 0x63, 0x65 };
            public static ReadOnlySpan<byte> KeepAlive => new byte[] { 0x4b, 0x65, 0x65, 0x70, 0x2d, 0x41, 0x6c, 0x69, 0x76, 0x65 };
            public static ReadOnlySpan<byte> LastModified => new byte[] { 0x4c, 0x61, 0x73, 0x74, 0x2d, 0x4d, 0x6f, 0x64, 0x69, 0x66, 0x69, 0x65, 0x64 };
            public static ReadOnlySpan<byte> Link => new byte[] { 0x4c, 0x69, 0x6e, 0x6b };
            public static ReadOnlySpan<byte> Location => new byte[] { 0x4c, 0x6f, 0x63, 0x61, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> MaxForwards => new byte[] { 0x4d, 0x61, 0x78, 0x2d, 0x46, 0x6f, 0x72, 0x77, 0x61, 0x72, 0x64, 0x73 };
            public static ReadOnlySpan<byte> Origin => new byte[] { 0x4f, 0x72, 0x69, 0x67, 0x69, 0x6e };
            public static ReadOnlySpan<byte> P3P => new byte[] { 0x50, 0x33, 0x50 };
            public static ReadOnlySpan<byte> Pragma => new byte[] { 0x50, 0x72, 0x61, 0x67, 0x6d, 0x61 };
            public static ReadOnlySpan<byte> ProxyAuthenticate => new byte[] { 0x50, 0x72, 0x6f, 0x78, 0x79, 0x2d, 0x41, 0x75, 0x74, 0x68, 0x65, 0x6e, 0x74, 0x69, 0x63, 0x61, 0x74, 0x65 };
            public static ReadOnlySpan<byte> ProxyAuthorization => new byte[] { 0x50, 0x72, 0x6f, 0x78, 0x79, 0x2d, 0x41, 0x75, 0x74, 0x68, 0x6f, 0x72, 0x69, 0x7a, 0x61, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> ProxyConnection => new byte[] { 0x50, 0x72, 0x6f, 0x78, 0x79, 0x2d, 0x43, 0x6f, 0x6e, 0x6e, 0x65, 0x63, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> ProxySupport => new byte[] { 0x50, 0x72, 0x6f, 0x78, 0x79, 0x2d, 0x53, 0x75, 0x70, 0x70, 0x6f, 0x72, 0x74 };
            public static ReadOnlySpan<byte> PublicKeyPins => new byte[] { 0x50, 0x75, 0x62, 0x6c, 0x69, 0x63, 0x2d, 0x4b, 0x65, 0x79, 0x2d, 0x50, 0x69, 0x6e, 0x73 };
            public static ReadOnlySpan<byte> Range => new byte[] { 0x52, 0x61, 0x6e, 0x67, 0x65 };
            public static ReadOnlySpan<byte> Referer => new byte[] { 0x52, 0x65, 0x66, 0x65, 0x72, 0x65, 0x72 };
            public static ReadOnlySpan<byte> ReferrerPolicy => new byte[] { 0x52, 0x65, 0x66, 0x65, 0x72, 0x72, 0x65, 0x72, 0x2d, 0x50, 0x6f, 0x6c, 0x69, 0x63, 0x79 };
            public static ReadOnlySpan<byte> Refresh => new byte[] { 0x52, 0x65, 0x66, 0x72, 0x65, 0x73, 0x68 };
            public static ReadOnlySpan<byte> RetryAfter => new byte[] { 0x52, 0x65, 0x74, 0x72, 0x79, 0x2d, 0x41, 0x66, 0x74, 0x65, 0x72 };
            public static ReadOnlySpan<byte> SecWebSocketAccept => new byte[] { 0x53, 0x65, 0x63, 0x2d, 0x57, 0x65, 0x62, 0x53, 0x6f, 0x63, 0x6b, 0x65, 0x74, 0x2d, 0x41, 0x63, 0x63, 0x65, 0x70, 0x74 };
            public static ReadOnlySpan<byte> SecWebSocketExtensions => new byte[] { 0x53, 0x65, 0x63, 0x2d, 0x57, 0x65, 0x62, 0x53, 0x6f, 0x63, 0x6b, 0x65, 0x74, 0x2d, 0x45, 0x78, 0x74, 0x65, 0x6e, 0x73, 0x69, 0x6f, 0x6e, 0x73 };
            public static ReadOnlySpan<byte> SecWebSocketKey => new byte[] { 0x53, 0x65, 0x63, 0x2d, 0x57, 0x65, 0x62, 0x53, 0x6f, 0x63, 0x6b, 0x65, 0x74, 0x2d, 0x4b, 0x65, 0x79 };
            public static ReadOnlySpan<byte> SecWebSocketProtocol => new byte[] { 0x53, 0x65, 0x63, 0x2d, 0x57, 0x65, 0x62, 0x53, 0x6f, 0x63, 0x6b, 0x65, 0x74, 0x2d, 0x50, 0x72, 0x6f, 0x74, 0x6f, 0x63, 0x6f, 0x6c };
            public static ReadOnlySpan<byte> SecWebSocketVersion => new byte[] { 0x53, 0x65, 0x63, 0x2d, 0x57, 0x65, 0x62, 0x53, 0x6f, 0x63, 0x6b, 0x65, 0x74, 0x2d, 0x56, 0x65, 0x72, 0x73, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> Server => new byte[] { 0x53, 0x65, 0x72, 0x76, 0x65, 0x72 };
            public static ReadOnlySpan<byte> ServerTiming => new byte[] { 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x2d, 0x54, 0x69, 0x6d, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> SetCookie => new byte[] { 0x53, 0x65, 0x74, 0x2d, 0x43, 0x6f, 0x6f, 0x6b, 0x69, 0x65 };
            public static ReadOnlySpan<byte> SetCookie2 => new byte[] { 0x53, 0x65, 0x74, 0x2d, 0x43, 0x6f, 0x6f, 0x6b, 0x69, 0x65, 0x32 };
            public static ReadOnlySpan<byte> StrictTransportSecurity => new byte[] { 0x53, 0x74, 0x72, 0x69, 0x63, 0x74, 0x2d, 0x54, 0x72, 0x61, 0x6e, 0x73, 0x70, 0x6f, 0x72, 0x74, 0x2d, 0x53, 0x65, 0x63, 0x75, 0x72, 0x69, 0x74, 0x79 };
            public static ReadOnlySpan<byte> TE => new byte[] { 0x54, 0x45 };
            public static ReadOnlySpan<byte> TSV => new byte[] { 0x54, 0x53, 0x56 };
            public static ReadOnlySpan<byte> Trailer => new byte[] { 0x54, 0x72, 0x61, 0x69, 0x6c, 0x65, 0x72 };
            public static ReadOnlySpan<byte> TransferEncoding => new byte[] { 0x54, 0x72, 0x61, 0x6e, 0x73, 0x66, 0x65, 0x72, 0x2d, 0x45, 0x6e, 0x63, 0x6f, 0x64, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> Upgrade => new byte[] { 0x55, 0x70, 0x67, 0x72, 0x61, 0x64, 0x65 };
            public static ReadOnlySpan<byte> UpgradeInsecureRequests => new byte[] { 0x55, 0x70, 0x67, 0x72, 0x61, 0x64, 0x65, 0x2d, 0x49, 0x6e, 0x73, 0x65, 0x63, 0x75, 0x72, 0x65, 0x2d, 0x52, 0x65, 0x71, 0x75, 0x65, 0x73, 0x74, 0x73 };
            public static ReadOnlySpan<byte> UserAgent => new byte[] { 0x55, 0x73, 0x65, 0x72, 0x2d, 0x41, 0x67, 0x65, 0x6e, 0x74 };
            public static ReadOnlySpan<byte> Vary => new byte[] { 0x56, 0x61, 0x72, 0x79 };
            public static ReadOnlySpan<byte> Via => new byte[] { 0x56, 0x69, 0x61 };
            public static ReadOnlySpan<byte> WWWAuthenticate => new byte[] { 0x57, 0x57, 0x57, 0x2d, 0x41, 0x75, 0x74, 0x68, 0x65, 0x6e, 0x74, 0x69, 0x63, 0x61, 0x74, 0x65 };
            public static ReadOnlySpan<byte> Warning => new byte[] { 0x57, 0x61, 0x72, 0x6e, 0x69, 0x6e, 0x67 };
            public static ReadOnlySpan<byte> XAspNetVersion => new byte[] { 0x58, 0x2d, 0x41, 0x73, 0x70, 0x4e, 0x65, 0x74, 0x2d, 0x56, 0x65, 0x72, 0x73, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> XCache => new byte[] { 0x58, 0x2d, 0x43, 0x61, 0x63, 0x68, 0x65 };
            public static ReadOnlySpan<byte> XContentDuration => new byte[] { 0x58, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x44, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6f, 0x6e };
            public static ReadOnlySpan<byte> XContentTypeOptions => new byte[] { 0x58, 0x2d, 0x43, 0x6f, 0x6e, 0x74, 0x65, 0x6e, 0x74, 0x2d, 0x54, 0x79, 0x70, 0x65, 0x2d, 0x4f, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x73 };
            public static ReadOnlySpan<byte> XFrameOptions => new byte[] { 0x58, 0x2d, 0x46, 0x72, 0x61, 0x6d, 0x65, 0x2d, 0x4f, 0x70, 0x74, 0x69, 0x6f, 0x6e, 0x73 };
            public static ReadOnlySpan<byte> XMSEdgeRef => new byte[] { 0x58, 0x2d, 0x4d, 0x53, 0x45, 0x64, 0x67, 0x65, 0x2d, 0x52, 0x65, 0x66 };
            public static ReadOnlySpan<byte> XPoweredBy => new byte[] { 0x58, 0x2d, 0x50, 0x6f, 0x77, 0x65, 0x72, 0x65, 0x64, 0x2d, 0x42, 0x79 };
            public static ReadOnlySpan<byte> XRequestID => new byte[] { 0x58, 0x2d, 0x52, 0x65, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2d, 0x49, 0x44 };
            public static ReadOnlySpan<byte> XUACompatible => new byte[] { 0x58, 0x2d, 0x55, 0x41, 0x2d, 0x43, 0x6f, 0x6d, 0x70, 0x61, 0x74, 0x69, 0x62, 0x6c, 0x65 };
            public static ReadOnlySpan<byte> XXssProtection => new byte[] { 0x58, 0x2d, 0x58, 0x53, 0x53, 0x2d, 0x50, 0x72, 0x6f, 0x74, 0x65, 0x63, 0x74, 0x69, 0x6f, 0x6e };

            public static (string Name, bool IsHttpContentHeader) FromSpan(ReadOnlySpan<byte> nameBytes)
            {
                if (nameBytes.IsEmpty) return (string.Empty, false);

                switch (nameBytes[0] | 0x20)
                {
                    case (byte)'a':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Accept)) { return ("Accept", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AcceptCharset)) { return ("Accept-Charset", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AcceptEncoding)) { return ("Accept-Encoding", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AcceptLanguage)) { return ("Accept-Language", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AcceptPatch)) { return ("Accept-Patch", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AcceptRanges)) { return ("Accept-Ranges", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlAllowCredentials)) { return ("Access-Control-Allow-Credentials", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlAllowHeaders)) { return ("Access-Control-Allow-Headers", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlAllowMethods)) { return ("Access-Control-Allow-Methods", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlAllowOrigin)) { return ("Access-Control-Allow-Origin", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlExposeHeaders)) { return ("Access-Control-Expose-Headers", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AccessControlMaxAge)) { return ("Access-Control-Max-Age", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Age)) { return ("Age", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Allow)) { return ("Allow", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AltSvc)) { return ("Alt-Svc", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, AltUsed)) { return ("Alt-Used", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Authorization)) { return ("Authorization", false); }
                        break;
                    case (byte)'b':
                        break;
                    case (byte)'c':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, CacheControl)) { return ("Cache-Control", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Connection)) { return ("Connection", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentDisposition)) { return ("Content-Disposition", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentEncoding)) { return ("Content-Encoding", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentLanguage)) { return ("Content-Language", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentLength)) { return ("Content-Length", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentLocation)) { return ("Content-Location", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentMD5)) { return ("Content-MD5", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentRange)) { return ("Content-Range", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentSecurityPolicy)) { return ("Content-Security-Policy", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ContentType)) { return ("Content-Type", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Cookie)) { return ("Cookie", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Cookie2)) { return ("Cookie2", false); }
                        break;
                    case (byte)'d':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Date)) { return ("Date", false); }
                        break;
                    case (byte)'e':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ETag)) { return ("ETag", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Expect)) { return ("Expect", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ExpectCT)) { return ("Expect-CT", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Expires)) { return ("Expires", true); }
                        break;
                    case (byte)'f':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, From)) { return ("From", false); }
                        break;
                    case (byte)'g':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, GrpcEncoding)) { return ("grpc-encoding", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, GrpcMessage)) { return ("grpc-message", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, GrpcStatus)) { return ("grpc-status", false); }
                        break;
                    case (byte)'h':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Host)) { return ("Host", false); }
                        break;
                    case (byte)'i':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, IfMatch)) { return ("If-Match", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, IfModifiedSince)) { return ("If-Modified-Since", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, IfNoneMatch)) { return ("If-None-Match", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, IfRange)) { return ("If-Range", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, IfUnmodifiedSince)) { return ("If-Unmodified-Since", false); }
                        break;
                    case (byte)'k':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, KeepAlive)) { return ("Keep-Alive", false); }
                        break;
                    case (byte)'l':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, LastModified)) { return ("Last-Modified", true); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Link)) { return ("Link", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Location)) { return ("Location", false); }
                        break;
                    case (byte)'m':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, MaxForwards)) { return ("Max-Forwards", false); }
                        break;
                    case (byte)'o':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Origin)) { return ("Origin", false); }
                        break;
                    case (byte)'p':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, P3P)) { return ("P3P", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Pragma)) { return ("Pragma", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ProxyAuthenticate)) { return ("Proxy-Authenticate", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ProxyAuthorization)) { return ("Proxy-Authorization", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ProxyConnection)) { return ("Proxy-Connection", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ProxySupport)) { return ("Proxy-Support", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, PublicKeyPins)) { return ("Public-Key-Pins", false); }
                        break;
                    case (byte)'r':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Range)) { return ("Range", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Referer)) { return ("Referer", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ReferrerPolicy)) { return ("Referrer-Policy", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Refresh)) { return ("Refresh", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, RetryAfter)) { return ("Retry-After", false); }
                        break;
                    case (byte)'s':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SecWebSocketAccept)) { return ("Sec-WebSocket-Accept", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SecWebSocketExtensions)) { return ("Sec-WebSocket-Extensions", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SecWebSocketKey)) { return ("Sec-WebSocket-Key", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SecWebSocketProtocol)) { return ("Sec-WebSocket-Protocol", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SecWebSocketVersion)) { return ("Sec-WebSocket-Version", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Server)) { return ("Server", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, ServerTiming)) { return ("Server-Timing", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SetCookie)) { return ("Set-Cookie", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, SetCookie2)) { return ("Set-Cookie2", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, StrictTransportSecurity)) { return ("Strict-Transport-Security", false); }
                        break;
                    case (byte)'t':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, TE)) { return ("TE", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, TSV)) { return ("TSV", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Trailer)) { return ("Trailer", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, TransferEncoding)) { return ("Transfer-Encoding", false); }
                        break;
                    case (byte)'u':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Upgrade)) { return ("Upgrade", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, UpgradeInsecureRequests)) { return ("Upgrade-Insecure-Requests", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, UserAgent)) { return ("User-Agent", false); }
                        break;
                    case (byte)'v':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Vary)) { return ("Vary", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Via)) { return ("Via", false); }
                        break;
                    case (byte)'w':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, WWWAuthenticate)) { return ("WWW-Authenticate", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, Warning)) { return ("Warning", false); }
                        break;
                    case (byte)'x':
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XAspNetVersion)) { return ("X-AspNet-Version", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XCache)) { return ("X-Cache", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XContentDuration)) { return ("X-Content-Duration", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XContentTypeOptions)) { return ("X-Content-Type-Options", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XFrameOptions)) { return ("X-Frame-Options", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XMSEdgeRef)) { return ("X-MSEdge-Ref", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XPoweredBy)) { return ("X-Powered-By", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XRequestID)) { return ("X-Request-ID", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XUACompatible)) { return ("X-UA-Compatible", false); }
                        if (UnsafeUtilities.EqualsIgnoreCase(nameBytes, XXssProtection)) { return ("X-XSS-Protection", false); }
                        break;
                }

                return (UnsafeUtilities.GetStringFromUtf8Bytes(nameBytes), false);
            }
        }
    }
}