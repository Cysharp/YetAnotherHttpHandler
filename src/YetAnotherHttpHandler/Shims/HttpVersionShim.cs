using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Cysharp.Net.Http.Shims
{
    internal class HttpVersionShim
    {
#if !NETSTANDARD2_0
        public static readonly Version Version20 = HttpVersion.Version20;
        public static readonly Version Unknown = HttpVersion.Unknown;
#else
        public static readonly Version Version20 = new Version(2, 0);
        public static readonly Version Unknown = new Version(0, 0);
#endif
    }
}
