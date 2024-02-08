using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cysharp.Net.Http
{
    internal sealed unsafe class YahaRuntimeSafeHandle : SafeHandle
    {
        public override bool IsInvalid => handle == IntPtr.Zero;

        public YahaRuntimeSafeHandle(YahaNativeRuntimeContext* runtime)
            : base((IntPtr)runtime, true)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public YahaNativeRuntimeContext* DangerousGet() => (YahaNativeRuntimeContext*)DangerousGetHandle();

        protected override bool ReleaseHandle()
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_dispose_runtime");
            NativeMethods.yaha_dispose_runtime((YahaNativeRuntimeContext*)handle);
            return true;
        }
    }

    internal sealed unsafe class YahaContextSafeHandle : SafeHandle
    {
        private YahaRuntimeSafeHandle? _parent;
        private readonly int _instanceId;

        public override bool IsInvalid => handle == IntPtr.Zero;

        public YahaContextSafeHandle(YahaNativeContext* context, int instanceId)
            : base((IntPtr)context, true)
        {
            _instanceId = instanceId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public YahaNativeContext* DangerousGet() => (YahaNativeContext*)DangerousGetHandle();

        public void SetParent(YahaRuntimeSafeHandle parent)
        {
            var addRef = false;
            parent.DangerousAddRef(ref addRef);
            if (addRef)
            {
                _parent = parent;
            }
        }

        protected override bool ReleaseHandle()
        {
            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[Id:{_instanceId}] yaha_dispose_context");
            NativeMethods.yaha_dispose_context((YahaNativeContext*)handle);

            _parent?.DangerousRelease();

            return true;
        }
    }
    
    internal sealed unsafe class YahaRequestContextSafeHandle : SafeHandle
    {
        private YahaContextSafeHandle? _parent;
        private readonly int _requestSequence;

        public override bool IsInvalid => handle == IntPtr.Zero;

        public YahaRequestContextSafeHandle(YahaNativeRequestContext* context, int requestSequence)
            : base((IntPtr)context, true)
        {
            _requestSequence = requestSequence;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public YahaNativeRequestContext* DangerousGet() => (YahaNativeRequestContext*)DangerousGetHandle();

        public void SetParent(YahaContextSafeHandle parent)
        {
            var addRef = false;
            parent.DangerousAddRef(ref addRef);
            if (addRef)
            {
                _parent = parent;
            }
        }

        protected override bool ReleaseHandle()
        {
            if (_parent is null)
            {
                return false;
            }

            if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"[ReqSeq:{_requestSequence}] yaha_request_destroy");
            NativeMethods.yaha_request_destroy((YahaNativeContext*)_parent.DangerousGetHandle(), (YahaNativeRequestContext*)handle);
            _parent.DangerousRelease();
            return true;
        }
    }
}
