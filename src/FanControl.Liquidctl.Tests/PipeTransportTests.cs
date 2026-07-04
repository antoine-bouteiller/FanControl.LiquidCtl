using System.Runtime.Versioning;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

public sealed class PipeTransportTests
{
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
}
