using Xunit;

namespace FanControl.LiquidCtl.Tests;

public sealed class BridgeProcessTests
{
    [WindowsOnlyFact]
    public void EnsureRunning_WithMissingExe_ReturnsFalseAndLogsOnce()
    {
        var logger = new FakeLogger();
        using var process = new BridgeProcess(logger);

        Assert.False(process.EnsureRunning());
        Assert.False(process.EnsureRunning());

        Assert.Single(logger.Messages, m => m.Contains("Missing", StringComparison.Ordinal));
    }

    [WindowsOnlyFact]
    public void Stop_WhenNeverStarted_DoesNotThrow()
    {
        using var process = new BridgeProcess(new FakeLogger());

        var ex = Record.Exception(process.Stop);

        Assert.Null(ex);
    }
}
