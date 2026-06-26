using FanControl.Plugins;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

internal sealed class FakeLiquidctlClient : ILiquidctlClient
{
    public bool Disposed { get; private set; }
    public bool InitCalled { get; private set; }
    public List<FixedSpeedRequest> SpeedRequests { get; } = [];
    public IReadOnlyList<DeviceStatus> StatusesToReturn { get; set; } = [];

    public void Init() => InitCalled = true;
    public IReadOnlyList<DeviceStatus> GetStatuses() => StatusesToReturn;
    public void SetFixedSpeed(FixedSpeedRequest req) => SpeedRequests.Add(req);
    public void Dispose() => Disposed = true;
}

internal sealed class FakeContainer : IPluginSensorsContainer
{
    public List<IPluginControlSensor> ControlSensors { get; } = [];
    public List<IPluginSensor> FanSensors { get; } = [];
    public List<IPluginSensor> TempSensors { get; } = [];
}

public sealed class LiquidctlPluginTests
{
    private static DeviceStatus MakeDevice(
        string description = "NZXT Kraken X63",
        IReadOnlyList<StatusValue>? status = null,
        IReadOnlyList<string>? speedChannels = null) =>
        new() { Id = 1, Description = description, Status = status ?? [], SpeedChannels = speedChannels ?? [] };

    private static StatusValue MakeStatus(string key = "Fan 1 speed", double? value = 1200.0, string unit = "rpm") =>
        new() { Key = key, Value = value, Unit = unit };

    [WindowsOnlyFact]
    public void Initialize_CallsInit()
    {
        using var client = new FakeLiquidctlClient();
        using var plugin = new LiquidCtlPlugin(client);

        plugin.Initialize();

        Assert.True(client.InitCalled);
    }

    [WindowsOnlyFact]
    public void Close_CallsDispose()
    {
        using var client = new FakeLiquidctlClient();
        using var plugin = new LiquidCtlPlugin(client);

        plugin.Close();

        Assert.True(client.Disposed);
    }

    [WindowsOnlyFact]
    public void Dispose_CallsClientDispose()
    {
        using var client = new FakeLiquidctlClient();
        var plugin = new LiquidCtlPlugin(client);

        plugin.Dispose();

        Assert.True(client.Disposed);
    }

    [WindowsOnlyFact]
    public void Update_EmptySensors_DoesNotThrow()
    {
        using var client = new FakeLiquidctlClient { StatusesToReturn = [MakeDevice(status: [MakeStatus()])] };
        using var plugin = new LiquidCtlPlugin(client);

        var ex = Record.Exception(plugin.Update);

        Assert.Null(ex);
    }

    [WindowsOnlyFact]
    public void Load_NoDevices_ContainerIsEmpty()
    {
        using var client = new FakeLiquidctlClient();
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Empty(container.FanSensors);
        Assert.Empty(container.TempSensors);
        Assert.Empty(container.ControlSensors);
    }

    [WindowsOnlyFact]
    public void Load_TempChannel_AddsTempSensor()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 27.5, unit: "°C")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Single(container.TempSensors);
        Assert.Empty(container.FanSensors);
        Assert.Empty(container.ControlSensors);
    }

    [WindowsOnlyFact]
    public void Load_FanChannel_AddsFanSensor()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Fan 1 speed", value: 1200.0, unit: "rpm")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Single(container.FanSensors);
        Assert.Empty(container.TempSensors);
        Assert.Empty(container.ControlSensors);
    }

    [WindowsOnlyFact]
    public void Load_DutyChannel_AddsControlSensor()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Fan 1 duty", value: 50.0, unit: "%")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Single(container.ControlSensors);
        Assert.Empty(container.FanSensors);
        Assert.Empty(container.TempSensors);
    }

    [WindowsOnlyFact]
    public void Load_NullChannelValue_SkipsSensor()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Fan 1 speed", value: null, unit: "rpm")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Empty(container.FanSensors);
    }

    [WindowsOnlyFact]
    public void Load_SpeedChannelNotInStatus_AddsAuthoritativeControl()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [], speedChannels: ["fan1"])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();

        plugin.Load(container);

        Assert.Single(container.ControlSensors);
    }

    [WindowsOnlyFact]
    public void Update_AfterLoad_UpdatesSensorValue()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 27.0, unit: "°C")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();
        plugin.Load(container);

        client.StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 30.0, unit: "°C")])];
        plugin.Update();

        Assert.Equal(30.0f, container.TempSensors[0].Value);
    }
}
