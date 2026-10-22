using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class CertificateVerificationCallbackLifetimeTest
{
    private const string ChildProcessEnvironmentVariable = "YAHA_CERTIFICATE_CALLBACK_LIFETIME_TEST_CHILD";

    [Fact]
    public async Task DisposeDuringTlsHandshake_DoesNotUseAnotherCertificateVerifier()
        => await RunIsolatedAsync(nameof(DisposeDuringTlsHandshake_DoesNotUseAnotherCertificateVerifier));

    private static async Task RunIsolatedAsync(string testName)
    {
        // A callback exception can terminate the process. Keep the test runner outside the reproducer.
        if (Environment.GetEnvironmentVariable(ChildProcessEnvironmentVariable) == testName)
        {
            await RunHandshakeScenarioAsync();
            return;
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(typeof(CertificateVerificationCallbackLifetimeTest).Assembly.Location);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add($"FullyQualifiedName={typeof(CertificateVerificationCallbackLifetimeTest).FullName}.{testName}");
        startInfo.Environment[ChildProcessEnvironmentVariable] = testName;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the test child process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The certificate callback lifetime test did not finish within 30 seconds.");
        }

        Assert.True(process.ExitCode == 0,
            $"The child process failed with exit code {process.ExitCode}.\n{await standardOutput}\n{await standardError}");
    }

    private static async Task RunHandshakeScenarioAsync()
    {
        using var certificate = new X509Certificate2(Path.Combine(AppContext.BaseDirectory, "Certificates", "localhost.pfx"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowHandshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = ServeOnceAsync();
        var replacementCalls = 0;
        ServerCertificateVerificationHandler strictVerifier = (_, _, _) => false;
        ServerCertificateVerificationHandler replacementVerifier = (_, _, _) =>
        {
            Interlocked.Increment(ref replacementCalls);
            return true;
        };

        using var handler = new YetAnotherHttpHandler { OnVerifyServerCertificate = strictVerifier };
        using var client = new HttpClient(handler, disposeHandler: false);
        var replacementHandles = new List<GCHandle>();

        try
        {
            var requestTask = client.GetAsync($"https://127.0.0.1:{port}/");
            await accepted.Task.WaitAsync(timeout.Token);

            // Capture the GCHandle owned by the native context before disposing the handler.
            // Dispose drains the request, so the handle may be freed even though the native
            // TLS task still retains the context; its closed callback gate must then reject
            // verification instead of invoking whatever now occupies the freed slot.
            var previousCallbackState = GetCallbackState(handler);
            handler.Dispose();
            var callbackRetainedAfterDispose = IsCallbackTarget(previousCallbackState, strictVerifier);
            AllocateReplacementTarget(replacementVerifier, previousCallbackState, callbackRetainedAfterDispose, replacementHandles);
            allowHandshake.SetResult();

            HttpResponseMessage? response = null;
            try
            {
                response = await requestTask.WaitAsync(timeout.Token);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
            {
                if (timeout.IsCancellationRequested)
                {
                    throw;
                }
            }

            using (response)
            {
                await serverTask.WaitAsync(timeout.Token);
                Assert.Equal(0, Volatile.Read(ref replacementCalls));
                Assert.Null(response);
            }
        }
        finally
        {
            allowHandshake.TrySetResult();
            listener.Stop();
            foreach (var replacementHandle in replacementHandles)
            {
                replacementHandle.Free();
            }
        }

        async Task ServeOnceAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            accepted.SetResult();
            await allowHandshake.Task.WaitAsync(timeout.Token);

            using var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                }, timeout.Token);

                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }

                await tls.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"u8.ToArray(), timeout.Token);
                await tls.FlushAsync(timeout.Token);
            }
            catch (AuthenticationException)
            {
                // The strict verifier is expected to reject the certificate.
            }
            catch (IOException)
            {
                // The client may close the connection after rejecting the certificate.
            }
        }
    }

    private static nint GetCallbackState(YetAnotherHttpHandler handler)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var core = typeof(YetAnotherHttpHandler).GetField("_handler", flags)?.GetValue(handler)
            ?? throw new InvalidOperationException("The native HTTP handler was not initialized.");
        var context = core.GetType().GetField("_handle", flags)?.GetValue(core)
            ?? throw new InvalidOperationException("The native context handle was not found.");
        var callbackHandle = context.GetType().GetField("_onVerifyServerCertificateHandle", flags)?.GetValue(context);
        if (callbackHandle is not GCHandle gcHandle || !gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The native context does not own a certificate verification callback.");
        }

        return GCHandle.ToIntPtr(gcHandle);
    }

    private static bool IsCallbackTarget(nint callbackState, ServerCertificateVerificationHandler verifier)
    {
        try
        {
            return ReferenceEquals(GCHandle.FromIntPtr(callbackState).Target, verifier);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void AllocateReplacementTarget(object replacement, nint previousCallbackState, bool callbackRetainedAfterDispose, List<GCHandle> replacementHandles)
    {
        if (callbackRetainedAfterDispose)
        {
            return;
        }

        // If the callback was released early, force its slot to be reused so that the
        // server response also exposes a verifier mix-up rather than a timing-dependent failure.
        for (var i = 0; i < 1024; i++)
        {
            var handle = GCHandle.Alloc(replacement);
            replacementHandles.Add(handle);
            if (GCHandle.ToIntPtr(handle) == previousCallbackState)
            {
                return;
            }
        }

        throw new InvalidOperationException("The released certificate callback slot could not be reused for the test.");
    }
}
