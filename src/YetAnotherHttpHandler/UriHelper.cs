using System;

namespace Cysharp.Net.Http
{
    internal static class UriHelper
    {
        public static string ToSafeUriString(Uri? uri)
        {
            if (uri is null) return string.Empty;

            var builder = new UriBuilder(uri);
            builder.Host = builder.Uri.IdnHost;
            return builder.ToString();
        }
    }
}
