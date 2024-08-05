using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using NUnit.Framework;

public abstract class YahaUnityTestBase
{
    protected YetAnotherHttpHandler CreateHandler()
        => new YetAnotherHttpHandler() { Http2Only = true };

    protected CancellationToken TimeoutToken { get; private set; }

    protected ValueTask<Server> LaunchServerAsync<T>(TestWebAppServerListenMode mode = TestWebAppServerListenMode.InsecureHttp2Only) => new(new Server(mode));

    protected class Server : IDisposable, IAsyncDisposable
    {
        private readonly TestWebAppServerListenMode _mode;

        public string BaseUri => _mode switch
        {
            TestWebAppServerListenMode.InsecureHttp1Only => "http://localhost:5115",
            TestWebAppServerListenMode.InsecureHttp2Only => "http://localhost:5116",
            _ => throw new NotSupportedException($"Unsupported mode '{_mode}'"),
        };

        public Server(TestWebAppServerListenMode mode)
        {
            _mode = mode;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }

    public enum TestWebAppServerListenMode
    {
        InsecureHttp1Only,
        InsecureHttp2Only,
        SecureHttp1Only,
        SecureHttp2Only,
        SecureHttp1AndHttp2,
    }

    protected class TestServerForHttp1AndHttp2
    {
        public const string SessionStateHeaderKey = "x-yahatest-session-id";
    }

    [SetUp]
    public void Setup()
    {
        TimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
    }
}