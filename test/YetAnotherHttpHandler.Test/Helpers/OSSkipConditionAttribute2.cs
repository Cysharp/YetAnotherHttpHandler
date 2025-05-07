using System.Reflection;
using System.Runtime.InteropServices;
using Xunit.Sdk;
using Xunit.v3;

namespace _YetAnotherHttpHandler.Test.Helpers;

internal class OSSkipConditionAttribute(OperatingSystems osPlatforms) : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (osPlatforms.HasFlag(GetCurrentOS()))
        {
            throw SkipException.ForSkip("The test is skipped on this platform");
        }
    }

    private static OperatingSystems GetCurrentOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OperatingSystems.Windows;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OperatingSystems.Linux;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OperatingSystems.MacOSX;
        }
        throw new PlatformNotSupportedException();
    }
}

[Flags]
public enum OperatingSystems
{
    Windows = 1,
    MacOSX = 1 << 1,
    Linux = 1 << 2,
}