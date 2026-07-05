using Xunit;

namespace FanControl.LiquidCtl.Tests;

public sealed class SensorMapperTests
{
    private static DeviceStatus MakeDevice(
        int id = 1,
        string description = "NZXT Smart Device",
        IReadOnlyList<StatusValue>? status = null,
        IReadOnlyList<string>? speedChannels = null) =>
        new() { Id = id, Description = description, Status = status ?? [], SpeedChannels = speedChannels ?? [] };

    private static StatusValue MakeStatus(string key = "Fan 1 speed", double? value = 1200.0, string unit = "rpm") =>
        new() { Key = key, Value = value, Unit = unit };

    [WindowsOnlyFact]
    public void Map_TwoIdenticalDevices_ProducesDistinctSensorIds()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus>
        {
            MakeDevice(id: 1, status: [MakeStatus()]),
            MakeDevice(id: 2, status: [MakeStatus()])
        };

        SensorSet mapped = SensorMapper.Map(devices, client);

        Assert.Equal(2, mapped.Fans.Count);
        Assert.NotEqual(mapped.Fans[0].Id, mapped.Fans[1].Id);
    }

    [WindowsOnlyFact]
    public void Map_UniqueDevice_KeepsPlainDescriptionInId()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus> { MakeDevice(status: [MakeStatus()]) };

        SensorSet mapped = SensorMapper.Map(devices, client);

        Assert.Equal("NZXTSmartDevice/Fan1speed", Assert.Single(mapped.Fans).Id);
    }

    [WindowsOnlyFact]
    public void EffectiveDescription_DuplicateDevice_AppendsDeviceId()
    {
        var devices = new List<DeviceStatus> { MakeDevice(id: 1), MakeDevice(id: 2) };
        ISet<string> duplicates = SensorMapper.DuplicateDescriptions(devices);

        Assert.Equal("NZXT Smart Device #2", SensorMapper.EffectiveDescription(devices[1], duplicates));
    }

    [WindowsOnlyFact]
    public void Map_SpeedChannelWithoutDutyStatus_AddsAuthoritativeControl()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus> { MakeDevice(speedChannels: ["fan1"]) };

        SensorSet mapped = SensorMapper.Map(devices, client);

        Assert.Single(mapped.Controls);
        Assert.Empty(mapped.Fans);
    }

    [WindowsOnlyFact]
    public void Map_SpeedChannelWithoutStatus_AuthoritativeControlHasNullPairedId()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus> { MakeDevice(speedChannels: ["fan1"]) };

        SensorSet mapped = SensorMapper.Map(devices, client);

        Assert.Null(Assert.Single(mapped.Controls).PairedFanSensorId);
    }

    [WindowsOnlyFact]
    public void Map_SpeedChannelWithDutyStatus_SkipsAuthoritativeControl()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus>
        {
            MakeDevice(
                status: [MakeStatus(key: "Fan 1 duty", value: 50.0, unit: "%")],
                speedChannels: ["fan1"])
        };

        SensorSet mapped = SensorMapper.Map(devices, client);

        Assert.Single(mapped.Controls);
    }

    [WindowsOnlyFact]
    public void Map_SpeedChannelWithSpeedStatus_AuthoritativeControlPairsWithFanSensor()
    {
        using var client = new FakeLiquidctlClient();
        var devices = new List<DeviceStatus>
        {
            MakeDevice(
                status: [MakeStatus(key: "Fan 1 speed", value: 1200.0, unit: "rpm")],
                speedChannels: ["fan1"])
        };

        SensorSet mapped = SensorMapper.Map(devices, client);

        DeviceSensor fanSensor = Assert.Single(mapped.Fans);
        ControlSensor control = Assert.Single(mapped.Controls);
        Assert.Equal(fanSensor.Id, control.PairedFanSensorId);
        Assert.Equal("NZXTSmartDevice/Fan1speed", control.PairedFanSensorId);
    }
}
