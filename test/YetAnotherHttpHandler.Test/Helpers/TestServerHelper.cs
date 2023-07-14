using System.Net;
using System.Net.Sockets;

namespace _YetAnotherHttpHandler.Test.Helpers;

public class TestServerHelper
{
    private static readonly HashSet<int> _usedPortInSession = new HashSet<int>();

    public static int GetUnusedEphemeralPort()
    {
        lock (_usedPortInSession)
        {
            var retryCount = 5;
            do
            {
                var port = GetUnusedEphemeralPortCore();
                if (!_usedPortInSession.Contains(port))
                {
                    _usedPortInSession.Add(port);
                    return port;
                }
            } while (retryCount-- > 0);

            throw new Exception("Cannot allocate unused port in this test session.");
        }

        static int GetUnusedEphemeralPortCore()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }
}