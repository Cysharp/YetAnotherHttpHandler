using Cysharp.Net.Http;
using JetBrains.Profiler.Api;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

MemoryProfiler.CollectAllocations(true);

using var httpHandler = new YetAnotherHttpHandler();
using var client = new HttpClient(httpHandler);

MemoryProfiler.GetSnapshot("Handler and HttpClient are created.");

for (var i = 0; i < 10; i++)
{
    Console.WriteLine($"Request: Begin ({i})");
    var result = await client.GetByteArrayAsync("https://cysharp.co.jp");
    MemoryProfiler.GetSnapshot($"Request End ({i})");
    Console.WriteLine($"Request: End ({i})");
    await Task.Delay(1000);
}

public class NativeLibraryResolver
{
    [ModuleInitializer]
    public static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(Cysharp.Net.Http.YetAnotherHttpHandler).Assembly, (name, assembly, path) =>
        {
            var ext = "";
            var prefix = "";
            var platform = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                platform = "win";
                prefix = "";
                ext = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platform = "osx";
                prefix = "lib";
                ext = ".dylib";
            }
            else
            {
                platform = "linux";
                prefix = "lib";
                ext = ".so";
            }

            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                _ => throw new NotSupportedException(),
            };

            return NativeLibrary.Load(Path.Combine($"runtimes/{platform}-{arch}/native/{prefix}{name}{ext}"));
        });
    }
}