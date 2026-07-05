using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

[Collection("NamedPipe")]
public sealed class PipeTransportTests
{
    private const string PipeName = "LiquidCtlPipe";

    [WindowsOnlyFact]
    public void Request_AfterDispose_ReturnsNull()
    {
        var transport = new PipeTransport(new FakeLogger());
        transport.Dispose();

        Assert.Null(transport.Request([1, 2, 3]));
    }

    [WindowsOnlyFact]
    public void Dispose_IsIdempotent()
    {
        var transport = new PipeTransport(new FakeLogger());

        transport.Dispose();
        var ex = Record.Exception(transport.Dispose);

        Assert.Null(ex);
    }

    // First request with no server fails and arms the reconnect backoff; the
    // second request inside the backoff window must short-circuit to null.
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void Request_WithinBackoffWindow_ReturnsNullWithoutRetrying()
    {
        var logger = new FakeLogger();
        using var transport = new PipeTransport(logger);

        Assert.Null(transport.Request([1]));
        Assert.Null(transport.Request([1]));

        Assert.Contains(logger.Messages, m => m.Contains("Pipe connection failed", StringComparison.Ordinal));
    }

    // Regression: a bridge that accepts a connection but never answers must
    // not freeze the caller forever - the injected timeout has to bound it.
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The server is owned and disposed by the worker thread via using.")]
    public void Request_ServerNeverResponds_ReturnsNullWithinTimeout()
    {
        using var hangUntil = new ManualResetEventSlim(false);
        var server = new NamedPipeServerStream(
            PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

        var serverThread = new Thread(() =>
        {
            using (server)
            {
                server.WaitForConnection();
                var buffer = new byte[65536];
                _ = server.Read(buffer, 0, buffer.Length);
                hangUntil.Wait();
            }
        });
        serverThread.Start();

        try
        {
            using var transport = new PipeTransport(new FakeLogger(), 1000);
            var stopwatch = Stopwatch.StartNew();

            byte[]? result = transport.Request([1, 2, 3]);

            stopwatch.Stop();
            Assert.Null(result);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                $"Request took {stopwatch.ElapsedMilliseconds}ms, expected it to be bounded by the timeout.");
        }
        finally
        {
            hangUntil.Set();
            serverThread.Join(5000);
        }
    }
}
