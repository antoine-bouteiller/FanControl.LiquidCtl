using System.Text.Json;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

public sealed class ModelsTests
{
    [Fact]
    public void PipeRequest_SerializesCommandKey()
    {
        var req = new PipeRequest { Command = "get.statuses" };
        var json = JsonSerializer.Serialize(req);
        Assert.Contains("\"command\"", json, StringComparison.Ordinal);
        Assert.Contains("get.statuses", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerResponse_SuccessStatus_IsSuccessTrue()
    {
        var json = """{"status":"success","data":null,"error":null}""";
        var response = JsonSerializer.Deserialize<ServerResponse<string>>(json);
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Null(response.Error);
    }

    [Fact]
    public void ServerResponse_ErrorStatus_IsSuccessFalse()
    {
        var json = """{"status":"error","data":null,"error":"device not found"}""";
        var response = JsonSerializer.Deserialize<ServerResponse<string>>(json);
        Assert.NotNull(response);
        Assert.False(response.IsSuccess);
        Assert.Equal("device not found", response.Error);
    }

    [Fact]
    public void StatusValue_DeserializesSnakeCaseFields()
    {
        var json = """{"key":"Liquid temperature","value":27.5,"unit":"°C"}""";
        var sv = JsonSerializer.Deserialize<StatusValue>(json);
        Assert.NotNull(sv);
        Assert.Equal("Liquid temperature", sv.Key);
        Assert.Equal(27.5, sv.Value);
        Assert.Equal("°C", sv.Unit);
    }

    [Fact]
    public void StatusValue_NullValue_DeserializesCorrectly()
    {
        var json = """{"key":"Pump mode","value":null,"unit":""}""";
        var sv = JsonSerializer.Deserialize<StatusValue>(json);
        Assert.NotNull(sv);
        Assert.Null(sv.Value);
    }

    [Fact]
    public void DeviceStatus_DeserializesWithSpeedChannels()
    {
        var json = """
        {
            "id": 2,
            "description": "Corsair Hydro H100i Platinum",
            "status": [{"key":"Fan 1 speed","value":1200,"unit":"rpm"}],
            "speed_channels": ["fan1","fan2"]
        }
        """;
        var ds = JsonSerializer.Deserialize<DeviceStatus>(json);
        Assert.NotNull(ds);
        Assert.Equal(2, ds.Id);
        Assert.Equal("Corsair Hydro H100i Platinum", ds.Description);
        Assert.Single(ds.Status);
        Assert.Equal(["fan1", "fan2"], ds.SpeedChannels);
    }

    [Fact]
    public void DeviceStatus_MissingSpeedChannels_DefaultsToEmpty()
    {
        var json = """{"id":1,"description":"NZXT Kraken X63","status":[]}""";
        var ds = JsonSerializer.Deserialize<DeviceStatus>(json);
        Assert.NotNull(ds);
        Assert.Empty(ds.SpeedChannels);
    }

    [Fact]
    public void FixedSpeedRequest_SerializesSnakeCasePropertyNames()
    {
        var req = new FixedSpeedRequest
        {
            DeviceId = 1,
            SpeedKwargs = new SpeedKwargs { Channel = "fan1", Duty = 75 }
        };
        var json = JsonSerializer.Serialize(req);
        Assert.Contains("\"device_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"speed_kwargs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"channel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"duty\"", json, StringComparison.Ordinal);
    }
}
