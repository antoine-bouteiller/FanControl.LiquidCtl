using System;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

[AttributeUsage(AttributeTargets.Method)]
sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "FanControl.Plugins.dll is Windows-only";
    }
}

public sealed class DeviceSensorTests
{
    private static DeviceStatus MakeDevice(string description = "NZXT Kraken X63") =>
        new() { Id = 1, Description = description, Status = [] };

    private static StatusValue MakeStatus(string key = "Fan 1 speed", double? value = 1200.0, string unit = "rpm") =>
        new() { Key = key, Value = value, Unit = unit };

    [WindowsOnlyFact]
    public void Constructor_SetsIdFromDescriptionAndKey()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(key: "Liquid temperature"));
        Assert.Equal("NZXTKrakenX63/Liquidtemperature", sensor.Id);
    }

    [WindowsOnlyFact]
    public void Constructor_SetsValueFromChannel()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: 1200.0));
        Assert.Equal(1200f, sensor.Value);
    }

    [WindowsOnlyFact]
    public void Constructor_NullChannelValue_SensorValueIsNull()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: null));
        Assert.Null(sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_ChangesValueToNewStatus()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: 1000.0));
        sensor.Update(MakeStatus(value: 1500.0));
        Assert.Equal(1500f, sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_WithNullValue_SetsValueToNull()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: 800.0));
        sensor.Update(MakeStatus(value: null));
        Assert.Null(sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_NoArg_ValueUnchanged()
    {
        var sensor = new DeviceSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: 900.0));
        sensor.Update();
        Assert.Equal(900f, sensor.Value);
    }
}

public sealed class ControlSensorTests
{
    private static DeviceStatus MakeDevice(string description = "NZXT Kraken X63") =>
        new() { Id = 1, Description = description, Status = [] };

    private static StatusValue MakeStatus(string key = "fan1 duty", double? value = 50.0, string unit = "%") =>
        new() { Key = key, Value = value, Unit = unit };

    [WindowsOnlyFact]
    public void Set_SendsCorrectDeviceIdAndDuty()
    {
        using var client = new FakeLiquidctlClient();
        var sensor = new ControlSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(), client, pairedFanSensorId: null, explicitChannelName: "fan1");

        sensor.Set(75.0f);

        Assert.Single(client.SpeedRequests);
        Assert.Equal(1, client.SpeedRequests[0].DeviceId);
        Assert.Equal(75, client.SpeedRequests[0].SpeedKwargs.Duty);
        Assert.Equal("fan1", client.SpeedRequests[0].SpeedKwargs.Channel);
    }

    [WindowsOnlyFact]
    public void Set_RoundsFloatDuty()
    {
        using var client = new FakeLiquidctlClient();
        var sensor = new ControlSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(), client, null, "fan1");

        sensor.Set(49.7f);

        Assert.Equal(50, client.SpeedRequests[0].SpeedKwargs.Duty);
    }

    [WindowsOnlyFact]
    public void Reset_CallsSetWithInitialValue()
    {
        using var client = new FakeLiquidctlClient();
        var sensor = new ControlSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: 60.0), client, null, "pump");

        sensor.Reset();

        Assert.Single(client.SpeedRequests);
        Assert.Equal(60, client.SpeedRequests[0].SpeedKwargs.Duty);
    }

    [WindowsOnlyFact]
    public void Reset_NullInitialValue_DoesNothing()
    {
        using var client = new FakeLiquidctlClient();
        var sensor = new ControlSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(value: null), client, null, "pump");

        sensor.Reset();

        Assert.Empty(client.SpeedRequests);
    }

    [WindowsOnlyFact]
    public void PairedFanSensorId_IsSetCorrectly()
    {
        using var client = new FakeLiquidctlClient();
        var sensor = new ControlSensor(MakeDevice(), "NZXT Kraken X63", MakeStatus(), client, "NZXTKrakenX63/Fan1speed");

        Assert.Equal("NZXTKrakenX63/Fan1speed", sensor.PairedFanSensorId);
    }
}
