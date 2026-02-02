using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace _YetAnotherHttpHandler.Test.Helpers;

public class NativeLibraryResolver
{
    [ModuleInitializer]
    public static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(Cysharp.Net.Http.YetAnotherHttpHandler).Assembly, static (name, assembly, path) => Resolver(name, assembly, path));
    }

    public static DllImportResolver Resolver { get; set; } = (name, assembly, path) =>
    {
        if (!name.Contains("yaha_native") && !name.Contains("Cysharp.Net.Http.YetAnotherHttpHandler.Native"))
        {
            return nint.Zero;
        }

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
    };
}
