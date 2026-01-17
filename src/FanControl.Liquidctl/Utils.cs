using System.Text.RegularExpressions;

namespace FanControl.LiquidCtl
{
    public static partial class Utils
    {
        [GeneratedRegex(@"^fan\s*(speed|duty)$", RegexOptions.IgnoreCase)]
        private static partial Regex SingleFanPattern();

        [GeneratedRegex(@"^pump\s*(speed|duty)$", RegexOptions.IgnoreCase)]
        private static partial Regex SinglePumpPattern();

        [GeneratedRegex(@"^pump\s*fan\s*(speed|duty)$", RegexOptions.IgnoreCase)]
        private static partial Regex PumpFanPattern();

        [GeneratedRegex(@"^fan\s*(\d+)\s*(speed|duty)$", RegexOptions.IgnoreCase)]
        private static partial Regex MultipleFanPattern();

        [GeneratedRegex(@"^fan\s*(speed|duty)\s*(\d+)$", RegexOptions.IgnoreCase)]
        private static partial Regex MultipleFanCorsairPattern();

        [GeneratedRegex(@"\bduty\b", RegexOptions.IgnoreCase)]
        private static partial Regex DutyWordPattern();

        [GeneratedRegex(@"\s*duty\s*", RegexOptions.IgnoreCase)]
        private static partial Regex DutyWithSpacesPattern();

        public static string CreateSensorId(string deviceDescription, string channelKey)
        {
            return $"{deviceDescription}/{channelKey}".Replace(" ", "", StringComparison.Ordinal);
        }

        public static string CreateSensorName(string deviceDescription, string channelKey)
        {
            var cleanedKey = DutyWithSpacesPattern().Replace(channelKey, " ").Trim();
            return $"{deviceDescription}: {cleanedKey}";
        }

        public static string ExtractChannelName(string statusKey)
        {
            if (string.IsNullOrWhiteSpace(statusKey))
            {
                return statusKey;
            }

            if (SingleFanPattern().IsMatch(statusKey))
            {
                return "fan";
            }

            if (SinglePumpPattern().IsMatch(statusKey))
            {
                return "pump";
            }

            if (PumpFanPattern().IsMatch(statusKey))
            {
                return "pump-fan";
            }

            var multipleFanMatch = MultipleFanPattern().Match(statusKey);
            if (multipleFanMatch.Success)
            {
                return $"fan{multipleFanMatch.Groups[1].Value}";
            }

            var corsairMatch = MultipleFanCorsairPattern().Match(statusKey);
            if (corsairMatch.Success)
            {
                return $"fan{corsairMatch.Groups[2].Value}";
            }

            return statusKey;
        }

        public static string GetSpeedKeyFromDutyKey(string dutyKey)
        {
            return DutyWordPattern().Replace(dutyKey, "speed");
        }
    }
}
