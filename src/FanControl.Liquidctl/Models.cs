using Newtonsoft.Json;

namespace FanControl.LiquidCtl
{
    internal static class BridgeConfig
    {
        public const int PipeConnectTimeoutMs = 2000;
        public const int RequestTimeoutMs = 5000;
        public const int MaxConnectRetries = 3;
        public const int RetryDelayMs = 500;
    }

    public class PipeRequest
    {
        [JsonProperty("command")]
        public required string Command { get; set; }

        [JsonProperty("data")]
        public FixedSpeedRequest? Data { get; set; }
    }

    public class SpeedKwargs
    {
        [JsonProperty("channel")]
        public required string Channel { get; set; }

        [JsonProperty("duty")]
        public required int Duty { get; set; }
    }

    public class FixedSpeedRequest
    {
        [JsonProperty("device_id")]
        public required int DeviceId { get; set; }

        [JsonProperty("speed_kwargs")]
        public required SpeedKwargs SpeedKwargs { get; set; }
    }

    public class BridgeResponse<T>
    {
        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("data")]
        public T? Data { get; set; }

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    public class StatusValue
    {
        [JsonProperty("key")]
        public required string Key { get; set; }

        [JsonProperty("value")]
        public double? Value { get; set; }

        [JsonProperty("unit")]
        public required string Unit { get; set; }
    }

    public class DeviceStatus
    {
        [JsonProperty("id")]
        public required int Id { get; set; }

        [JsonProperty("description")]
        public required string Description { get; set; }

        [JsonProperty("status")]
        public required IReadOnlyCollection<StatusValue> Status { get; init; }
    }
}
