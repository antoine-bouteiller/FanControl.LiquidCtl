using System.Collections.Concurrent;
using System.Text.Json;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public sealed class LiquidctlClient(IPluginLogger logger) : ILiquidctlClient
    {
        private readonly IPluginLogger _logger = logger;
        private readonly BridgeProcess _process = new(logger);
        private readonly PipeTransport _transport = new(logger);

        private readonly ConcurrentDictionary<(int DeviceId, string Channel), FixedSpeedRequest> _pendingSpeeds = new();
        private int _speedWorkerActive;

        private CachedStatuses? _cachedStatuses;
        private volatile bool _disposed;

        public void Init()
        {
            for (int attempt = 1; attempt <= BridgeConfig.MaxInitRetries; attempt++)
            {
                if (_process.EnsureRunning())
                {
                    Thread.Sleep(BridgeConfig.BridgeStartupDelayMs);
                    // An empty device list is a valid outcome; only a transport
                    // failure (null) is worth retrying.
                    if (RequestStatuses() != null) return;
                }

                _logger.Log($"[LiquidCtl] Init attempt {attempt}/{BridgeConfig.MaxInitRetries} failed, retrying in {BridgeConfig.RetryDelayMs}ms...");
                _transport.ResetBackoff();
                Thread.Sleep(BridgeConfig.RetryDelayMs);
            }
        }

        public IReadOnlyList<DeviceStatus> GetStatuses()
        {
            List<DeviceStatus>? statuses = RequestStatuses();
            if (statuses != null) return statuses;

            if (!_disposed) _process.EnsureRunning();

            // Serve the cache only while fresh: FanControl must see sensors go
            // stale instead of steering fans off a frozen temperature reading.
            CachedStatuses? cached = _cachedStatuses;
            return cached is { IsExpired: false } ? cached.Statuses : [];
        }

        public void SetFixedSpeed(FixedSpeedRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (_disposed) return;

            // Coalesce per channel (only the latest duty matters) and drain from
            // a single worker so writes cannot arrive out of order.
            _pendingSpeeds[(request.DeviceId, request.SpeedKwargs.Channel)] = request;
            if (Interlocked.CompareExchange(ref _speedWorkerActive, 1, 0) == 0)
            {
                Task.Run(DrainPendingSpeeds);
            }
        }

        private void DrainPendingSpeeds()
        {
            do
            {
                while (!_pendingSpeeds.IsEmpty)
                {
                    foreach ((int DeviceId, string Channel) key in _pendingSpeeds.Keys)
                    {
                        if (_pendingSpeeds.TryRemove(key, out FixedSpeedRequest? request))
                        {
                            SendRequest<object>(new PipeRequest { Command = "set.fixed_speed", Data = request });
                        }
                    }
                }
                Interlocked.Exchange(ref _speedWorkerActive, 0);
                // Re-claim the worker slot if a request slipped in after the empty
                // check, otherwise it would sit unsent until the next Set call.
            } while (!_pendingSpeeds.IsEmpty && Interlocked.CompareExchange(ref _speedWorkerActive, 1, 0) == 0);
        }

        private List<DeviceStatus>? RequestStatuses()
        {
            var statuses = SendRequest<List<DeviceStatus>>(new PipeRequest { Command = "get.statuses" });
            if (statuses != null) _cachedStatuses = new CachedStatuses(statuses);
            return statuses;
        }

        private T? SendRequest<T>(PipeRequest request) where T : class
        {
            if (_disposed) return null;

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request);
            byte[]? responseBytes = _transport.Request(payload);
            if (responseBytes == null) return null;

            try
            {
                var response = JsonSerializer.Deserialize<ServerResponse<T>>(responseBytes);
                if (response?.IsSuccess == true) return response.Data;
                _logger.Log($"[LiquidCtl] Bridge error: {response?.Error}");
            }
            catch (JsonException ex)
            {
                _logger.Log($"[LiquidCtl] Invalid response: {ex.Message}");
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.Dispose();
            _process.Dispose();
        }
    }
}
