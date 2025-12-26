using Cysharp.Net.Http;
using HttpClientTestServer;

namespace _YetAnotherHttpHandler.Test;

[OSSkipCondition(OperatingSystems.Windows)]
public class UdsTest(ITestOutputHelper testOutputHelper) : Http2TestBase(testOutputHelper)
{
    private readonly string _udsPath = Path.Combine(Path.GetTempPath(), $"yaha-uds-test-{Guid.NewGuid()}");
    protected override YetAnotherHttpHandler CreateHandler()
    {
        return new YetAnotherHttpHandler()
        {
            UnixDomainSocketPath = _udsPath,
            Http2Only = true,
        };
    }

    protected override Task<ITestServer> LaunchServerAsync()
        => LaunchServerAsync(new TestServerOptions(ListenHttpProtocols.Http2, isSecure: true)
        {
            UnixDomainSocketPath =  _udsPath,
        });

}

[OSSkipCondition(OperatingSystems.MacOSX | OperatingSystems.Linux)]
public class UdsNotSupportedTest
{
    [Fact]
    public async Task Throw()
    {
        // Arrange
        using var handler = new YetAnotherHttpHandler()
        {
            UnixDomainSocketPath = "/path/to/socket",
        };
        using var httpClient = new HttpClient(handler);

        // Act & Assert
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
        {
            await httpClient.GetAsync("http://localhost/");
        });
    }
}
