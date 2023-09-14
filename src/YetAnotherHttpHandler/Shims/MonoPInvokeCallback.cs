using System;

namespace Cysharp.Net.Http.Shims
{
#if !UNITY_2019_1_OR_NEWER
    [AttributeUsage(AttributeTargets.Method)]
    internal class MonoPInvokeCallback : Attribute
    {
        public MonoPInvokeCallback(Type t) { }
    }
#endif
}