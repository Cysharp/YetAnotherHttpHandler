using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace _YetAnotherHttpHandler.Test;

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