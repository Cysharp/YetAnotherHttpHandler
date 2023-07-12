using System;

namespace Cysharp.Net.Http
{
#if !UNITY_2019
    [AttributeUsage(AttributeTargets.Method)]
    internal class MonoPInvokeCallback : Attribute
    {
        public MonoPInvokeCallback(Type t) { }
    }
#endif
}