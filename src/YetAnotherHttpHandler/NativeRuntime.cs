using System;
using System.Collections.Generic;
using System.Text;

namespace Cysharp.Net.Http
{
    internal class NativeRuntime
    {
        public static NativeRuntime Instance { get; } = new NativeRuntime();

        private readonly object _lock = new object();
        internal /* for unit testing */ int _refCount;
        private YahaRuntimeSafeHandle? _handle;
        private int? _workerThreads = null;

        private NativeRuntime()
        { }

        public void SetWorkerThreads(int? workerThreads)
        {
            _workerThreads = workerThreads;
        }

        public unsafe YahaRuntimeSafeHandle Acquire()
        {
            lock (_lock)
            {
                if (_refCount == 0)
                {
                    if (YahaEventSource.Log.IsEnabled()) YahaEventSource.Log.Info($"yaha_init_runtime");
                    var runtime = NativeMethods.yaha_init_runtime(_workerThreads ?? -1);
                    _handle = new YahaRuntimeSafeHandle(runtime);
                }

                _refCount++;

                return _handle!;
            }
        }

        public void Release()
        {
            lock (_lock)
            {
                _refCount--;
                if (_refCount == 0)
                {
                    _handle?.Dispose();
                    _handle = null;
                }
            }
        }
    }
}
