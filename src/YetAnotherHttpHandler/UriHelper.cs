using System;

namespace Cysharp.Net.Http
{
    internal static class UriHelper
    {
        public static string ToSafeUriString(Uri uri)
        {
            var builder = new UriBuilder(uri);
            builder.Host = builder.Uri.IdnHost;
            return builder.ToString();
        }
    }
}
