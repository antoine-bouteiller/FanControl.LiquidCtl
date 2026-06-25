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
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(key: "Liquid temperature"));
        Assert.Equal("NZXTKrakenX63/Liquidtemperature", sensor.Id);
    }

    [WindowsOnlyFact]
    public void Constructor_SetsValueFromChannel()
    {
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(value: 1200.0));
        Assert.Equal(1200f, sensor.Value);
    }

    [WindowsOnlyFact]
    public void Constructor_NullChannelValue_SensorValueIsNull()
    {
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(value: null));
        Assert.Null(sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_ChangesValueToNewStatus()
    {
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(value: 1000.0));
        sensor.Update(MakeStatus(value: 1500.0));
        Assert.Equal(1500f, sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_WithNullValue_SetsValueToNull()
    {
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(value: 800.0));
        sensor.Update(MakeStatus(value: null));
        Assert.Null(sensor.Value);
    }

    [WindowsOnlyFact]
    public void Update_NoArg_ValueUnchanged()
    {
        var sensor = new DeviceSensor(MakeDevice(), MakeStatus(value: 900.0));
        sensor.Update();
        Assert.Equal(900f, sensor.Value);
    }
}
