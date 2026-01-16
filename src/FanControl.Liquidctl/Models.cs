using MessagePack;

namespace FanControl.LiquidCtl
{
    internal static class BridgeConfig
    {
        public const int PipeConnectTimeoutMs = 2000;
        public const int RequestTimeoutMs = 5000;
        public const int MaxConnectRetries = 3;
        public const int RetryDelayMs = 500;
    }

    [MessagePackObject]
    public class PipeRequest
    {
        [Key("command")]
        public required string Command { get; set; }

        [Key("data")]
        public FixedSpeedRequest? Data { get; set; }
    }

    [MessagePackObject]
    public class SpeedKwargs
    {
        [Key("channel")]
        public required string Channel { get; set; }

        [Key("duty")]
        public required int Duty { get; set; }
    }

    [MessagePackObject]
    public class FixedSpeedRequest
    {
        [Key("device_id")]
        public required int DeviceId { get; set; }

        [Key("speed_kwargs")]
        public required SpeedKwargs SpeedKwargs { get; set; }
    }

    [MessagePackObject]
    public class BridgeResponse<T>
    {
        [Key("status")]
        public string? Status { get; set; }

        [Key("data")]
        public T? Data { get; set; }

        [Key("error")]
        public string? Error { get; set; }
    }

    [MessagePackObject]
    public class StatusValue
    {
        [Key("key")]
        public required string Key { get; set; }

        [Key("value")]
        public double? Value { get; set; }

        [Key("unit")]
        public required string Unit { get; set; }
    }

    [MessagePackObject]
    public class DeviceStatus
    {
        [Key("id")]
        public required int Id { get; set; }

        [Key("bus")]
        public required string Bus { get; set; }

        [Key("address")]
        public required string Address { get; set; }

        [Key("description")]
        public required string Description { get; set; }

        [Key("status")]
        public required IReadOnlyCollection<StatusValue> Status { get; init; }
    }
}
