using Xunit;

namespace FanControl.LiquidCtl.Tests
{
    // Test data uses the verbatim status keys and device descriptions reported by liquidctl,
    // gathered from the device guides at https://github.com/liquidctl/liquidctl/tree/main/docs.
    public sealed class UtilsCreateSensorIdTests
    {
        [Theory]
        [InlineData("NZXT Kraken X63", "Liquid temperature", "NZXTKrakenX63/Liquidtemperature")]
        [InlineData("Corsair Hydro H100i Platinum", "Fan 1 speed", "CorsairHydroH100iPlatinum/Fan1speed")]
        [InlineData("MSI MPG Coreliquid K360", "Water block duty", "MSIMPGCoreliquidK360/Waterblockduty")]
        [InlineData("ASUS ROG Ryujin II 360", "External fan duty", "ASUSROGRyujinII360/Externalfanduty")]
        [InlineData("Aquacomputer Octo", "Fan 1 power", "AquacomputerOcto/Fan1power")]
        [InlineData("", "", "/")]
        public void CreateSensorId_RemovesAllSpacesAndJoinsWithSlash(
            string deviceDescription, string channelKey, string expected)
        {
            Assert.Equal(expected, Utils.CreateSensorId(deviceDescription, channelKey));
        }
    }

    public sealed class UtilsCreateSensorNameTests
    {
        [Theory]
        // "duty" (with surrounding spaces) stripped from the key
        [InlineData("NZXT Kraken X3", "Pump duty", "NZXT Kraken X3: Pump")]
        [InlineData("Corsair Hydro H100i Platinum", "Fan 1 duty", "Corsair Hydro H100i Platinum: Fan 1")]
        [InlineData("ASUS ROG Ryujin II 360", "Pump fan duty", "ASUS ROG Ryujin II 360: Pump fan")]
        [InlineData("MSI MPG Coreliquid K360", "Water block duty", "MSI MPG Coreliquid K360: Water block")]
        // no "duty" in key -> key passes through unchanged
        [InlineData("NZXT Kraken X63", "Liquid temperature", "NZXT Kraken X63: Liquid temperature")]
        [InlineData("Corsair Commander Core", "Fan speed 1", "Corsair Commander Core: Fan speed 1")]
        // empty channel key
        [InlineData("NZXT Kraken X63", "", "NZXT Kraken X63: ")]
        public void CreateSensorName_StripsDutyWordAndFormatsName(
            string deviceDescription, string channelKey, string expected)
        {
            Assert.Equal(expected, Utils.CreateSensorName(deviceDescription, channelKey));
        }
    }

    public sealed class UtilsGetSpeedKeyFromDutyKeyTests
    {
        [Theory]
        // Real duty keys -> their paired speed (rpm) key
        [InlineData("Pump duty", "Pump speed")]
        [InlineData("Fan 1 duty", "Fan 1 speed")]
        [InlineData("Water block duty", "Water block speed")]
        [InlineData("Pump fan duty", "Pump fan speed")]
        [InlineData("External fan duty", "External fan speed")]
        [InlineData("Fan duty 1", "Fan speed 1")] // Corsair Commander Core ordering
        // No "duty" present -> passthrough
        [InlineData("Liquid temperature", "Liquid temperature")]
        [InlineData("", "")]
        public void GetSpeedKeyFromDutyKey_ReplacesDutyWordBoundaryWithSpeed(
            string dutyKey, string expected)
        {
            Assert.Equal(expected, Utils.GetSpeedKeyFromDutyKey(dutyKey));
        }
    }

    public sealed class UtilsExtractChannelNameTests
    {
        // --- Null / empty / whitespace edge cases ---

        [Fact]
        public void ExtractChannelName_NullInput_ReturnsNull()
        {
            Assert.Null(Utils.ExtractChannelName(null!));
        }

        [Fact]
        public void ExtractChannelName_EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", Utils.ExtractChannelName(""));
        }

        [Fact]
        public void ExtractChannelName_WhitespaceOnly_ReturnsWhitespace()
        {
            Assert.Equal("   ", Utils.ExtractChannelName("   "));
        }

        // --- Single "fan" channel ---
        // NZXT Kraken X2/M2 & Z3, Aquacomputer D5 Next, Asetek Pro/690LC, Corsair HXi/RMi PSU

        [Theory]
        [InlineData("Fan speed")]
        [InlineData("Fan duty")]
        public void ExtractChannelName_SingleFan_ReturnsFan(string input)
        {
            Assert.Equal("fan", Utils.ExtractChannelName(input));
        }

        // --- Single "pump" channel ---
        // NZXT Kraken X3/Z3 & X2/M2, ASUS Ryujin II, MSI Coreliquid, Corsair Commander Core,
        // Aquacomputer D5 Next, Asetek Pro

        [Theory]
        [InlineData("Pump duty")]
        [InlineData("Pump speed")]
        public void ExtractChannelName_SinglePump_ReturnsPump(string input)
        {
            Assert.Equal("pump", Utils.ExtractChannelName(input));
        }

        // --- "pump-fan" channel (ASUS Ryujin II embedded fan) ---

        [Theory]
        [InlineData("Pump fan duty")]
        [InlineData("Pump fan speed")]
        public void ExtractChannelName_PumpFan_ReturnsPumpFan(string input)
        {
            Assert.Equal("pump-fan", Utils.ExtractChannelName(input));
        }

        // --- "external-fans" channel (ASUS Ryujin II AIO fan controller) ---

        [Theory]
        [InlineData("External fan duty")]
        public void ExtractChannelName_ExternalFan_ReturnsExternalFans(string input)
        {
            Assert.Equal("external-fans", Utils.ExtractChannelName(input));
        }

        // --- "waterblock-fan" channel (MSI MPG Coreliquid water-block fan) ---

        [Theory]
        [InlineData("Water block duty")]
        [InlineData("Water block speed")]
        public void ExtractChannelName_WaterBlock_ReturnsWaterBlockFan(string input)
        {
            Assert.Equal("waterblock-fan", Utils.ExtractChannelName(input));
        }

        // --- Numbered fans "fanN" (key: "Fan N speed|duty") ---
        // Corsair Hydro Platinum/Pro XT & Commander Pro, MSI Coreliquid, Aquacomputer Octo/Quadro,
        // Lian Li Uni, NZXT Smart Device V1/V2 & Grid+ V3

        [Theory]
        [InlineData("Fan 1 duty", "fan1")]
        [InlineData("Fan 2 duty", "fan2")]
        [InlineData("Fan 1 speed", "fan1")]
        [InlineData("Fan 6 speed", "fan6")]
        [InlineData("Fan 8 duty", "fan8")]
        public void ExtractChannelName_NumberedFan_ReturnsFanNumber(string input, string expected)
        {
            Assert.Equal(expected, Utils.ExtractChannelName(input));
        }

        // --- Corsair Commander Core ordering "fanN" (key: "Fan speed|duty N") ---

        [Theory]
        [InlineData("Fan speed 1", "fan1")]
        [InlineData("Fan duty 2", "fan2")]
        [InlineData("Fan speed 6", "fan6")]
        public void ExtractChannelName_CorsairCoreFan_ReturnsFanNumber(string input, string expected)
        {
            Assert.Equal(expected, Utils.ExtractChannelName(input));
        }

        // --- Pass-through: read-only / unrecognized keys returned unchanged ---
        // Real keys reported across NZXT, Corsair, Aquacomputer, ASUS, Asetek and PSU devices.

        [Theory]
        [InlineData("Liquid temperature")]
        [InlineData("Water temperature")]
        [InlineData("Soft. Sensor 1")]
        [InlineData("Sensor 3")]
        [InlineData("Temperature 1")]
        [InlineData("Flow sensor")]
        [InlineData("Pump mode")]
        [InlineData("Noise level")]
        [InlineData("Fan 1 control mode")]
        [InlineData("VRM temperature")]
        [InlineData("+12V output voltage")]
        [InlineData("Fan 1 voltage")]
        [InlineData("Fan 1 power")]
        // Numbered external fan reads (ASUS Ryujin II) must NOT collapse to "external-fans"
        [InlineData("External fan 1 speed")]
        public void ExtractChannelName_UnknownKey_ReturnsInputUnchanged(string input)
        {
            Assert.Equal(input, Utils.ExtractChannelName(input));
        }

        // --- Priority ordering guards ---

        [Fact]
        public void ExtractChannelName_PumpFanSpeed_DoesNotMatchSingleFanOrPump()
        {
            Assert.Equal("pump-fan", Utils.ExtractChannelName("Pump fan speed"));
        }

        [Fact]
        public void ExtractChannelName_Fan1Speed_DoesNotMatchSingleFanPattern()
        {
            Assert.Equal("fan1", Utils.ExtractChannelName("Fan 1 speed"));
        }
    }

    public sealed class UtilsGetDutyKeyFromSpeedKeyTests
    {
        [Theory]
        // Real speed (rpm) keys -> their paired duty key
        [InlineData("Pump speed", "Pump duty")]
        [InlineData("Fan 1 speed", "Fan 1 duty")]
        [InlineData("Water block speed", "Water block duty")]
        [InlineData("Pump fan speed", "Pump fan duty")]
        [InlineData("External fan speed", "External fan duty")]
        [InlineData("Fan speed 1", "Fan duty 1")] // Corsair Commander Core ordering
        // No "speed" present -> passthrough
        [InlineData("Liquid temperature", "Liquid temperature")]
        [InlineData("", "")]
        public void GetDutyKeyFromSpeedKey_ReplacesSpeedWordBoundaryWithDuty(
            string speedKey, string expected)
        {
            Assert.Equal(expected, Utils.GetDutyKeyFromSpeedKey(speedKey));
        }
    }
}
