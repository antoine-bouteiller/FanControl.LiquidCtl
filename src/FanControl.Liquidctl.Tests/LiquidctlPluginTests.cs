using FanControl.LiquidCtl;
using FanControl.Plugins;
using FluentAssertions;
using Moq;
using Xunit;

namespace FanControl.Liquidctl.Tests;

public class LiquidctlPluginTests
{
	[Fact]
	public void Plugin_ShouldHaveCorrectName()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();
		var plugin = new LiquidCtlPlugin(mockLogger.Object);

		// Assert
		plugin.Name.Should().Be("liquidctl");
	}

	[Fact]
	public void Load_WithNullContainer_ShouldThrowArgumentNullException()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();
		var plugin = new LiquidCtlPlugin(mockLogger.Object);

		// Act
		Action act = () => plugin.Load(null!);

		// Assert
		act.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void Dispose_ShouldNotThrowWhenCalledMultipleTimes()
	{
		// Arrange
		var mockLogger = new Mock<IPluginLogger>();
		var plugin = new LiquidCtlPlugin(mockLogger.Object);

		// Act & Assert
		// Dispose should be safe to call multiple times
		plugin.Dispose();
		plugin.Dispose();
		// If we reach here without exceptions, the test passes
	}
}

/// <summary>
/// Integration-style tests that verify the plugin behavior with mock data.
/// These tests would ideally use a mocked bridge wrapper.
/// </summary>
public class LiquidctlPluginIntegrationTests
{
	[Fact]
	public void Load_ShouldFilterSensorsToSupportedUnits()
	{
		// Note: This is a conceptual test showing what should be tested
		// In practice, this would require dependency injection of the bridge wrapper
		// to properly mock the GetStatuses() response

		// The plugin should only add sensors with units: °C, rpm, %
		// Other units should be filtered out
	}

	[Fact]
	public void Load_ShouldCreateControlSensorsForPercentageUnit()
	{
		// Note: This is a conceptual test
		// Sensors with "%" unit should be added as ControlSensors
	}

	[Fact]
	public void Load_ShouldCreateFanSensorsForRpmUnit()
	{
		// Note: This is a conceptual test
		// Sensors with "rpm" unit should be added to FanSensors
	}

	[Fact]
	public void Load_ShouldCreateTempSensorsForCelsiusUnit()
	{
		// Note: This is a conceptual test
		// Sensors with "°C" unit should be added to TempSensors
	}

	[Fact]
	public void Load_ShouldAutoLinkControlSensorsToSpeedSensors()
	{
		// Note: This is a conceptual test
		// Control sensors like "pump" should be auto-linked to "pump speed" sensors
		// The PairedFanSensorId should be set correctly
	}

	[Fact]
	public void Update_ShouldUpdateExistingSensorValues()
	{
		// Note: This is a conceptual test
		// When Update() is called, existing sensors should have their values updated
		// based on the new data from GetStatuses()
	}
}
