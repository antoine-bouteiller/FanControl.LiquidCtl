using Newtonsoft.Json;

namespace FanControl.LiquidCtl
{
    /// <summary>
    /// Bridge configuration constants adapted from CoolerControl patterns.
    /// </summary>
    internal static class BridgeConfig
    {
        // Connection timeouts
        public const int PipeConnectTimeoutMs = 2000;
        public const int RequestTimeoutMs = 5000;
        public const int HandshakeTimeoutMs = 2000;

        // Retry configuration (inspired by CoolerControl: 7 request retries, 5 init retries)
        public const int MaxRequestRetries = 5;
        public const int MaxInitRetries = 3;
        public const int RetryDelayMs = 1000;

        // Startup timing
        public const int BridgeStartupDelayMs = 5000;

        // Shutdown
        public const int ShutdownTimeoutMs = 3000;

        // Status cache
        public const int StatusCacheExpiryMs = 2000;
    }

    /// <summary>
    /// Connection state for health monitoring.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Ready,
        Faulted
    }

    /// <summary>
    /// Request sent to the bridge.
    /// </summary>
    internal sealed class PipeRequest
    {
        [JsonProperty("command")]
        public required string Command { get; init; }

        [JsonProperty("data")]
        public object? Data { get; init; }
    }

    /// <summary>
    /// Speed control parameters.
    /// </summary>
    public sealed class SpeedKwargs
    {
        [JsonProperty("channel")]
        public required string Channel { get; init; }

        [JsonProperty("duty")]
        public required int Duty { get; init; }
    }

    /// <summary>
    /// Request to set a fixed speed on a device.
    /// </summary>
    public sealed class FixedSpeedRequest
    {
        [JsonProperty("device_id")]
        public required int DeviceId { get; init; }

        [JsonProperty("speed_kwargs")]
        public required SpeedKwargs SpeedKwargs { get; init; }
    }

    /// <summary>
    /// Response from the bridge.
    /// </summary>
#pragma warning disable CA1812 // Instantiated by JSON deserialization
    internal sealed class BridgeResponse<T>
#pragma warning restore CA1812
    {
        [JsonProperty("status")]
        public string? Status { get; init; }

        [JsonProperty("data")]
        public T? Data { get; init; }

        [JsonProperty("error")]
        public string? Error { get; init; }

        public bool IsSuccess => Status == "success";
    }

    /// <summary>
    /// Handshake response for connection verification.
    /// </summary>
#pragma warning disable CA1812 // Instantiated by JSON deserialization
    internal sealed class HandshakeResponse
#pragma warning restore CA1812
    {
        [JsonProperty("shake")]
        public bool Shake { get; init; }
    }

    /// <summary>
    /// A single status value from a device.
    /// </summary>
    public sealed class StatusValue
    {
        [JsonProperty("key")]
        public required string Key { get; init; }

        [JsonProperty("value")]
        public double? Value { get; init; }

        [JsonProperty("unit")]
        public required string Unit { get; init; }
    }

    /// <summary>
    /// Status of a single device.
    /// </summary>
    public sealed class DeviceStatus
    {
        [JsonProperty("id")]
        public required int Id { get; init; }

        [JsonProperty("description")]
        public required string Description { get; init; }

        [JsonProperty("status")]
        public required IReadOnlyList<StatusValue> Status { get; init; }
    }

    /// <summary>
    /// Cached device statuses with timestamp for expiry checking.
    /// </summary>
    internal sealed class CachedStatuses
    {
        public IReadOnlyList<DeviceStatus> Statuses { get; }
        public DateTime Timestamp { get; }

        public CachedStatuses(IReadOnlyList<DeviceStatus> statuses)
        {
            Statuses = statuses;
            Timestamp = DateTime.UtcNow;
        }

        public bool IsExpired => (DateTime.UtcNow - Timestamp).TotalMilliseconds > BridgeConfig.StatusCacheExpiryMs;
    }
}
