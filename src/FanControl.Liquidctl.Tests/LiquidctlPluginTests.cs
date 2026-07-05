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
    public void Update_TwoIdenticalDevices_UpdatesEachSensorIndependently()
    {
        static DeviceStatus Dev(int id, double value) => new()
        {
            Id = id,
            Description = "NZXT Smart Device",
            Status = [new StatusValue { Key = "Liquid temperature", Value = value, Unit = "°C" }]
        };
        using var client = new FakeLiquidctlClient { StatusesToReturn = [Dev(1, 20.0), Dev(2, 25.0)] };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();
        plugin.Load(container);

        client.StatusesToReturn = [Dev(1, 30.0), Dev(2, 35.0)];
        plugin.Update();

        Assert.Equal(2, container.TempSensors.Count);
        Assert.Equal(30.0f, container.TempSensors[0].Value);
        Assert.Equal(35.0f, container.TempSensors[1].Value);
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

    [WindowsOnlyFact]
    public void Update_BridgeDies_SensorValueGoesNull()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 27.0, unit: "°C")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();
        plugin.Load(container);

        client.StatusesToReturn = [];
        plugin.Update();

        Assert.Null(container.TempSensors[0].Value);
    }

    [WindowsOnlyFact]
    public void Update_BridgeRecovers_SensorValueRepopulated()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 27.0, unit: "°C")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();
        plugin.Load(container);

        client.StatusesToReturn = [];
        plugin.Update();
        client.StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 32.0, unit: "°C")])];
        plugin.Update();

        Assert.Equal(32.0f, container.TempSensors[0].Value);
    }

    [WindowsOnlyFact]
    public void Update_TwoIdenticalDevices_OneDropsOut_SurvivorKeepsUpdatingAndOtherGoesNull()
    {
        static DeviceStatus Dev(int id, double value) => new()
        {
            Id = id,
            Description = "NZXT Smart Device",
            Status = [new StatusValue { Key = "Liquid temperature", Value = value, Unit = "°C" }]
        };
        using var client = new FakeLiquidctlClient { StatusesToReturn = [Dev(1, 20.0), Dev(2, 25.0)] };
        using var plugin = new LiquidCtlPlugin(client);
        var container = new FakeContainer();
        plugin.Load(container);

        client.StatusesToReturn = [Dev(1, 30.0)];
        plugin.Update();

        Assert.Equal(30.0f, container.TempSensors[0].Value);
        Assert.Null(container.TempSensors[1].Value);
    }

    [WindowsOnlyFact]
    public void Update_UnknownDevice_RaisesRefreshRequestedOnce()
    {
        using var client = new FakeLiquidctlClient { StatusesToReturn = [MakeDevice()] };
        using var plugin = new LiquidCtlPlugin(client);
        int refreshCount = 0;
        plugin.RefreshRequested += () => refreshCount++;
        plugin.Load(new FakeContainer());

        DeviceStatus unknownDevice = new() { Id = 2, Description = "New Device", Status = [] };
        client.StatusesToReturn = [MakeDevice(), unknownDevice];
        plugin.Update();
        plugin.Update();

        Assert.Equal(1, refreshCount);
    }

    [WindowsOnlyFact]
    public void Update_UnknownDeviceAfterReload_RaisesRefreshRequestedAgain()
    {
        using var client = new FakeLiquidctlClient { StatusesToReturn = [MakeDevice()] };
        using var plugin = new LiquidCtlPlugin(client);
        int refreshCount = 0;
        plugin.RefreshRequested += () => refreshCount++;
        plugin.Load(new FakeContainer());

        DeviceStatus newDevice = new() { Id = 2, Description = "New Device", Status = [] };
        client.StatusesToReturn = [MakeDevice(), newDevice];
        plugin.Update();
        plugin.Load(new FakeContainer());
        DeviceStatus thirdDevice = new() { Id = 3, Description = "Third Device", Status = [] };
        client.StatusesToReturn = [MakeDevice(), newDevice, thirdDevice];
        plugin.Update();

        Assert.Equal(2, refreshCount);
    }

    [WindowsOnlyFact]
    public void Load_CalledTwice_DoesNotDuplicateSensors()
    {
        using var client = new FakeLiquidctlClient
        {
            StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 27.0, unit: "°C")])]
        };
        using var plugin = new LiquidCtlPlugin(client);
        plugin.Load(new FakeContainer());
        var secondContainer = new FakeContainer();

        plugin.Load(secondContainer);
        client.StatusesToReturn = [MakeDevice(status: [MakeStatus(key: "Liquid temperature", value: 30.0, unit: "°C")])];
        plugin.Update();

        Assert.Equal(30.0f, Assert.Single(secondContainer.TempSensors).Value);
    }
}
