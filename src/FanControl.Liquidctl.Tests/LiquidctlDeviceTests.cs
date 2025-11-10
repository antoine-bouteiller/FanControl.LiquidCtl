using FanControl.LiquidCtl;
using FluentAssertions;
using Moq;
using Xunit;

namespace FanControl.Liquidctl.Tests;

public class DeviceSensorTests
{
	[Fact]
	public void DeviceSensor_ShouldGenerateCorrectId()
	{
		// Arrange
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "Liquid temperature",
			Value = 28.5,
			Unit = "°C"
		};

		// Act
		var sensor = new DeviceSensor(device, channel);

		// Assert
		sensor.Id.Should().Be("NZXTKrakenX53/Liquidtemperature");
	}

	[Fact]
	public void DeviceSensor_ShouldGenerateCorrectName()
	{
		// Arrange
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "Liquid temperature",
			Value = 28.5,
			Unit = "°C"
		};

		// Act
		var sensor = new DeviceSensor(device, channel);

		// Assert
		sensor.Name.Should().Be("NZXT Kraken X53: Liquid temperature");
	}

	[Fact]
	public void DeviceSensor_ShouldReturnCorrectValue()
	{
		// Arrange
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "Liquid temperature",
			Value = 28.5,
			Unit = "°C"
		};

		// Act
		var sensor = new DeviceSensor(device, channel);

		// Assert
		sensor.Value.Should().Be(28.5f);
	}

	[Fact]
	public void DeviceSensor_Update_ShouldUpdateChannelValue()
	{
		// Arrange
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var initialChannel = new StatusValue
		{
			Key = "Liquid temperature",
			Value = 28.5,
			Unit = "°C"
		};
		var sensor = new DeviceSensor(device, initialChannel);

		var updatedChannel = new StatusValue
		{
			Key = "Liquid temperature",
			Value = 30.2,
			Unit = "°C"
		};

		// Act
		sensor.Update(updatedChannel);

		// Assert
		sensor.Value.Should().Be(30.2f);
		sensor.Channel.Should().BeSameAs(updatedChannel);
	}
}

public class ControlSensorTests
{
	[Fact]
	public void ControlSensor_ShouldStoreInitialValue()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump",
			Value = 50.0,
			Unit = "%"
		};

		// Act
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);

		// Assert
		sensor.Initial.Should().Be(50.0f);
	}

	[Fact]
	public void ControlSensor_Set_ShouldCallBridgeWrapperWithCorrectParameters()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump duty",
			Value = 50.0,
			Unit = "%"
		};
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);

		// Act
		sensor.Set(75.0f);

		// Assert
		mockWrapper.Verify(w => w.SetFixedSpeed(It.Is<FixedSpeedRequest>(req =>
			req.DeviceId == 1 &&
			req.SpeedKwargs.Channel == "pumpduty" &&
			req.SpeedKwargs.Duty == 75
		)), Times.Once);
	}

	[Fact]
	public void ControlSensor_Set_ShouldRoundDutyValue()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump",
			Value = 50.0,
			Unit = "%"
		};
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);

		// Act
		sensor.Set(75.6f);

		// Assert
		mockWrapper.Verify(w => w.SetFixedSpeed(It.Is<FixedSpeedRequest>(req =>
			req.SpeedKwargs.Duty == 76
		)), Times.Once);
	}

	[Fact]
	public void ControlSensor_Reset_ShouldSetToInitialValue()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump",
			Value = 50.0,
			Unit = "%"
		};
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);
		sensor.Set(75.0f);

		// Act
		sensor.Reset();

		// Assert
		mockWrapper.Verify(w => w.SetFixedSpeed(It.Is<FixedSpeedRequest>(req =>
			req.SpeedKwargs.Duty == 50
		)), Times.Once);
	}

	[Fact]
	public void ControlSensor_Reset_ShouldNotSetIfInitialIsNull()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump",
			Value = null,
			Unit = "%"
		};
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);

		// Act
		sensor.Reset();

		// Assert
		mockWrapper.Verify(w => w.SetFixedSpeed(It.IsAny<FixedSpeedRequest>()), Times.Never);
	}

	[Fact]
	public void ControlSensor_PairedFanSensorId_ShouldBeSettable()
	{
		// Arrange
		var mockWrapper = new Mock<LiquidctlBridgeWrapper>(Mock.Of<FanControl.Plugins.IPluginLogger>());
		var device = new DeviceStatus
		{
			Id = 1,
			Bus = "hid",
			Address = "1234",
			Description = "NZXT Kraken X53",
			Status = []
		};
		var channel = new StatusValue
		{
			Key = "pump",
			Value = 50.0,
			Unit = "%"
		};
		var sensor = new ControlSensor(device, channel, mockWrapper.Object);

		// Act
		sensor.PairedFanSensorId = "NZXTKrakenX53/pumpspeed";

		// Assert
		sensor.PairedFanSensorId.Should().Be("NZXTKrakenX53/pumpspeed");
	}
}
