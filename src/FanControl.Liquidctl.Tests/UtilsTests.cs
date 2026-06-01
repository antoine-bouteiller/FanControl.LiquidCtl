using Xunit;

namespace FanControl.LiquidCtl.Tests
{
    public sealed class UtilsCreateSensorIdTests
    {
        [Theory]
        [InlineData("Corsair H100i", "fan speed", "CorsairH100i/fanspeed")]
        [InlineData("NZXT Kraken Z", "pump duty", "NZXTKrakenZ/pumpduty")]
        [InlineData("Device", "channel", "Device/channel")]
        [InlineData("No Spaces", "no spaces", "NoSpaces/nospaces")]
        [InlineData("A B C", "x y", "ABC/xy")]
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
        // "duty" surrounded by spaces -> collapsed to single space -> trimmed
        [InlineData("Corsair H100i", "fan duty speed", "Corsair H100i: fan speed")]
        [InlineData("Device", "pump duty", "Device: pump")]
        [InlineData("Device", "fan duty", "Device: fan")]
        // no "duty" in key -> key passes through unchanged
        [InlineData("Corsair H100i", "fan speed", "Corsair H100i: fan speed")]
        [InlineData("Device", "pump speed", "Device: pump speed")]
        // "duty" embedded between words
        [InlineData("Dev", "pump duty 1", "Dev: pump 1")]
        // "duty" with leading/trailing spaces -> empty after trim
        [InlineData("Dev", " duty ", "Dev: ")]
        [InlineData("Dev", "", "Dev: ")]
        public void CreateSensorName_StripsDutyWordAndFormatsName(
            string deviceDescription, string channelKey, string expected)
        {
            Assert.Equal(expected, Utils.CreateSensorName(deviceDescription, channelKey));
        }
    }

    public sealed class UtilsGetSpeedKeyFromDutyKeyTests
    {
        [Theory]
        // Word boundary: "duty" -> "speed"
        [InlineData("fan duty", "fan speed")]
        [InlineData("pump duty", "pump speed")]
        [InlineData("Duty", "speed")] // case-insensitive
        [InlineData("DUTY", "speed")]
        [InlineData("fan DUTY 1", "fan speed 1")]
        // "duty" not at word boundary -> not replaced
        [InlineData("fan dutymode", "fan dutymode")]
        [InlineData("fan induty", "fan induty")]
        // No "duty" at all -> passthrough
        [InlineData("fan speed", "fan speed")]
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

        // --- SingleFanPattern: ^fan\s*(speed|duty)$ ---

        [Theory]
        [InlineData("fan speed")]
        [InlineData("fan duty")]
        [InlineData("Fan Speed")]
        [InlineData("FAN DUTY")]
        [InlineData("fan  speed")]
        public void ExtractChannelName_SingleFan_ReturnsFan(string input)
        {
            Assert.Equal("fan", Utils.ExtractChannelName(input));
        }

        // --- SinglePumpPattern: ^pump\s*(speed|duty)$ ---

        [Theory]
        [InlineData("pump speed")]
        [InlineData("pump duty")]
        [InlineData("Pump Speed")]
        [InlineData("PUMP DUTY")]
        [InlineData("pump  speed")]
        public void ExtractChannelName_SinglePump_ReturnsPump(string input)
        {
            Assert.Equal("pump", Utils.ExtractChannelName(input));
        }

        // --- PumpFanPattern: ^pump\s*fan\s*(speed|duty)$ ---

        [Theory]
        [InlineData("pump fan speed")]
        [InlineData("pump fan duty")]
        [InlineData("Pump Fan Speed")]
        [InlineData("PUMP FAN DUTY")]
        [InlineData("pumpfan speed")]
        [InlineData("pump  fan  speed")]
        public void ExtractChannelName_PumpFan_ReturnsPumpFan(string input)
        {
            Assert.Equal("pump-fan", Utils.ExtractChannelName(input));
        }

        // --- MultipleFanPattern: ^fan\s*(\d+)\s*(speed|duty)$ -> Groups[1] ---

        [Theory]
        [InlineData("fan 1 speed", "fan1")]
        [InlineData("fan 2 duty", "fan2")]
        [InlineData("fan1 speed", "fan1")]
        [InlineData("fan12 duty", "fan12")]
        [InlineData("Fan 3 Speed", "fan3")]
        [InlineData("fan 10 speed", "fan10")]
        public void ExtractChannelName_MultipleFan_ReturnsFanNumber(string input, string expected)
        {
            Assert.Equal(expected, Utils.ExtractChannelName(input));
        }

        // --- MultipleFanCorsairPattern: ^fan\s*(speed|duty)\s*(\d+)$ -> Groups[2] ---

        [Theory]
        [InlineData("fan speed 1", "fan1")]
        [InlineData("fan duty 2", "fan2")]
        [InlineData("fan speed 12", "fan12")]
        [InlineData("Fan Speed 3", "fan3")]
        [InlineData("FAN DUTY 4", "fan4")]
        [InlineData("fan speed1", "fan1")]
        public void ExtractChannelName_CorsairFan_ReturnsFanNumber(string input, string expected)
        {
            Assert.Equal(expected, Utils.ExtractChannelName(input));
        }

        // --- Pass-through: unknown keys returned as-is ---

        [Theory]
        [InlineData("temperature")]
        [InlineData("liquid temp")]
        [InlineData("noise level")]
        [InlineData("fan 1 temperature")]
        [InlineData("pump mode")]
        public void ExtractChannelName_UnknownKey_ReturnsInputUnchanged(string input)
        {
            Assert.Equal(input, Utils.ExtractChannelName(input));
        }

        // --- Priority ordering guards ---

        [Fact]
        public void ExtractChannelName_PumpFanSpeed_DoesNotMatchSingleFanOrPump()
        {
            Assert.Equal("pump-fan", Utils.ExtractChannelName("pump fan speed"));
        }

        [Fact]
        public void ExtractChannelName_Fan1Speed_DoesNotMatchSingleFanPattern()
        {
            Assert.Equal("fan1", Utils.ExtractChannelName("fan 1 speed"));
        }
    }
}
