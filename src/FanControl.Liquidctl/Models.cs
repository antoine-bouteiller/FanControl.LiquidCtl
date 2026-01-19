using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace FanControl.LiquidCtl
{
    internal static class BridgeConfig
    {
        public const int PipeConnectTimeoutMs = 2000;
        public const int RequestTimeoutMs = 5000;
        public const int HandshakeTimeoutMs = 2000;

        public const int MaxRequestRetries = 5;
        public const int MaxInitRetries = 3;
        public const int RetryDelayMs = 1000;

        public const int BridgeStartupDelayMs = 5000;

        public const int ShutdownTimeoutMs = 3000;

        public const int StatusCacheExpiryMs = 2000;
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Ready,
        Faulted
    }

    internal sealed class PipeRequest
    {
        [JsonPropertyName("command")]
        public required string Command { get; init; }

        [JsonPropertyName("data")]
        public object? Data { get; init; }
    }

    public sealed class SpeedKwargs
    {
        [JsonPropertyName("channel")]
        public required string Channel { get; init; }

        [JsonPropertyName("duty")]
        public required int Duty { get; init; }
    }

    public sealed class FixedSpeedRequest
    {
        [JsonPropertyName("device_id")]
        public required int DeviceId { get; init; }

        [JsonPropertyName("speed_kwargs")]
        public required SpeedKwargs SpeedKwargs { get; init; }
    }


    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by JsonSerializer")]
    internal sealed class ServerResponse<T>
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("data")]
        public T? Data { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonIgnore]
        public bool IsSuccess => Status == "success";
    }

    public sealed class StatusValue
    {
        [JsonPropertyName("key")]
        public required string Key { get; init; }

        [JsonPropertyName("value")]
        public double? Value { get; init; }

        [JsonPropertyName("unit")]
        public required string Unit { get; init; }
    }

    public sealed class DeviceStatus
    {
        [JsonPropertyName("id")]
        public required int Id { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("status")]
        public required IReadOnlyList<StatusValue> Status { get; init; }
    }

    internal sealed class CachedStatuses(IReadOnlyList<DeviceStatus> statuses)
    {
        public IReadOnlyList<DeviceStatus> Statuses { get; } = statuses;
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public bool IsExpired => (DateTime.UtcNow - Timestamp).TotalMilliseconds > BridgeConfig.StatusCacheExpiryMs;
    }
}
