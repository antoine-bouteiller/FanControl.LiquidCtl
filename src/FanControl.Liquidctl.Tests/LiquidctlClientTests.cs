using FanControl.Plugins;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

internal sealed class FakeLogger : IPluginLogger
{
    public List<string> Messages { get; } = [];
    public void Log(string message) => Messages.Add(message);
}

public sealed class LiquidctlClientTests
{
    [WindowsOnlyFact]
    public void State_Initially_Disconnected()
    {
        using var client = new LiquidctlClient(new FakeLogger());
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [WindowsOnlyFact]
    public void GetStatuses_WhenDisconnected_ReturnsEmpty()
    {
        using var client = new LiquidctlClient(new FakeLogger());
        Assert.Empty(client.GetStatuses());
    }

    [WindowsOnlyFact]
    public void Init_WithMissingBridgeExe_EndsInFaultedState()
    {
        using var client = new LiquidctlClient(new FakeLogger());

        client.Init();

        Assert.Equal(ConnectionState.Faulted, client.State);
    }

    [WindowsOnlyFact]
    public void Dispose_IsIdempotent()
    {
        var client = new LiquidctlClient(new FakeLogger());

        client.Dispose();
        var ex = Record.Exception(client.Dispose);

        Assert.Null(ex);
    }

    [WindowsOnlyFact]
    public void SetFixedSpeed_WhenDisconnected_DoesNotThrow()
    {
        using var client = new LiquidctlClient(new FakeLogger());
        var request = new FixedSpeedRequest
        {
            DeviceId = 1,
            SpeedKwargs = new SpeedKwargs { Channel = "fan1", Duty = 50 }
        };

        var ex = Record.Exception(() => client.SetFixedSpeed(request));

        Assert.Null(ex);
    }
}
