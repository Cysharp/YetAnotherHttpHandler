using System;
using System.Collections.Generic;
using System.Text;

// HACK: grpc-dotnet checks InnerException type names, so matching type name ensures behaviour
// https://github.com/grpc/grpc-dotnet/blob/v2.60.0/src/Grpc.Net.Client/Internal/GrpcProtocolHelpers.cs#L479

// ReSharper disable once CheckNamespace
namespace System.Net.Http
{
    internal class Http2StreamException : Exception
    {
        public Http2StreamException(string message, Exception innerException) : base(message, innerException) { }
    }
}
