using FanControl.LiquidCtl;
using FanControl.Plugins;
using FluentAssertions;
using Moq;
using Xunit;

namespace FanControl.Liquidctl.Tests;

public class LiquidctlBridgeWrapperTests
{
	[Fact]
	public void Constructor_ShouldNotThrow()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();

		// Act
		Action act = () => new LiquidctlBridgeWrapper(mockLogger.Object);

		// Assert
		act.Should().NotThrow();
	}

	[Fact]
	public void Dispose_ShouldNotThrowWhenCalledMultipleTimes()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();
		var wrapper = new LiquidctlBridgeWrapper(mockLogger.Object);

		// Act & Assert
		// Dispose should be safe to call multiple times
		wrapper.Dispose();
		wrapper.Dispose();
		// If we reach here without exceptions, the test passes
	}

	[Fact]
	public void Shutdown_ShouldHandleNullProcess()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();
		var wrapper = new LiquidctlBridgeWrapper(mockLogger.Object);

		// Act & Assert
		// Shutdown should not throw even if process was never started
		wrapper.Shutdown();
		// If we reach here without exceptions, the test passes
	}
}

/// <summary>
/// Tests for PipeRequest and related models
/// </summary>
public class PipeRequestTests
{
	[Fact]
	public void PipeRequest_ShouldSerializeCorrectly()
	{
		// Arrange
		var request = new PipeRequest
		{
			Command = "get.statuses"
		};

		// Act
		var json = Newtonsoft.Json.JsonConvert.SerializeObject(request);

		// Assert
		json.Should().Contain("\"command\":\"get.statuses\"");
	}

	[Fact]
	public void PipeRequest_WithData_ShouldSerializeCorrectly()
	{
		// Arrange
		var request = new PipeRequest
		{
			Command = "set.fixed_speed",
			Data = new FixedSpeedRequest
			{
				DeviceId = 1,
				SpeedKwargs = new SpeedKwargs
				{
					Channel = "pump",
					Duty = 75
				}
			}
		};

		// Act
		var json = Newtonsoft.Json.JsonConvert.SerializeObject(request);

		// Assert
		json.Should().Contain("\"command\":\"set.fixed_speed\"");
		json.Should().Contain("\"device_id\":1");
		json.Should().Contain("\"channel\":\"pump\"");
		json.Should().Contain("\"duty\":75");
	}

	[Fact]
	public void FixedSpeedRequest_ShouldSerializeWithSnakeCase()
	{
		// Arrange
		var request = new FixedSpeedRequest
		{
			DeviceId = 1,
			SpeedKwargs = new SpeedKwargs
			{
				Channel = "pump",
				Duty = 75
			}
		};

		// Act
		var json = Newtonsoft.Json.JsonConvert.SerializeObject(request);

		// Assert
		json.Should().Contain("\"device_id\"");
		json.Should().Contain("\"speed_kwargs\"");
	}
}

/// <summary>
/// Tests for DeviceStatus deserialization
/// </summary>
public class DeviceStatusTests
{
	[Fact]
	public void DeviceStatus_ShouldDeserializeFromJson()
	{
		// Arrange
		var json = """
		{
			"id": 1,
			"bus": "hid",
			"address": "1234:5678:00",
			"description": "NZXT Kraken X53",
			"status": [
				{
					"key": "Liquid temperature",
					"value": 28.5,
					"unit": "°C"
				},
				{
					"key": "pump speed",
					"value": 2500,
					"unit": "rpm"
				}
			]
		}
		""";

		// Act
		var device = System.Text.Json.JsonSerializer.Deserialize<DeviceStatus>(json);

		// Assert
		device.Should().NotBeNull();
		device!.Id.Should().Be(1);
		device.Bus.Should().Be("hid");
		device.Address.Should().Be("1234:5678:00");
		device.Description.Should().Be("NZXT Kraken X53");
		device.Status.Should().HaveCount(2);
		device.Status.First().Key.Should().Be("Liquid temperature");
		device.Status.First().Value.Should().Be(28.5);
		device.Status.First().Unit.Should().Be("°C");
	}

	[Fact]
	public void StatusValue_WithNullValue_ShouldDeserialize()
	{
		// Arrange
		var json = """
		{
			"key": "firmware",
			"value": null,
			"unit": ""
		}
		""";

		// Act
		var status = System.Text.Json.JsonSerializer.Deserialize<StatusValue>(json);

		// Assert
		status.Should().NotBeNull();
		status!.Key.Should().Be("firmware");
		status.Value.Should().BeNull();
		status.Unit.Should().Be("");
	}
}
