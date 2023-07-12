using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Cysharp.Net.Http
{
    internal static class TrailingHeadersShim
    {
        // NOTE: We need to match the property name of grpc-dotnet.
        // https://github.com/grpc/grpc-dotnet/blob/v2.55.x/src/Shared/TrailingHeadersHelpers.cs#L52
        private const string ResponseTrailersKey = "__ResponseTrailers";

        public static HttpHeaders TrailingHeaders(this HttpResponseMessage response)
        {
#if !NETSTANDARD2_0
            return response.TrailingHeaders;
#else
            if (response.RequestMessage.Properties.TryGetValue(ResponseTrailersKey, out var responseTrailers) && responseTrailers is HttpHeaders)
            {
                return (HttpHeaders)responseTrailers;
            }

            return ResponseTrailers.Empty;
#endif
        }

#if NETSTANDARD2_0
        public static void EnsureTrailingHeaders(this HttpResponseMessage response)
        {
            if (!response.RequestMessage.Properties.ContainsKey(ResponseTrailersKey))
            {
                response.RequestMessage.Properties[ResponseTrailersKey] = new ResponseTrailers();
            }
        }
#endif

        class ResponseTrailers : HttpHeaders
        {
            public static readonly HttpHeaders Empty = new ResponseTrailers();
        }
    }
}
