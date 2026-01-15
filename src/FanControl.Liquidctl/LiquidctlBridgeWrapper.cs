using MessagePack;
using System.Diagnostics;
using System.IO.Pipes;
using FanControl.Plugins;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace FanControl.LiquidCtl
{
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

    public class LiquidctlBridgeWrapper : IDisposable
    {
        private readonly IPluginLogger _logger;
        private static readonly string _exePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "liquidctl_bridge.exe");
        private const string _pipeName = "LiquidCtlPipe";

        private Process? _bridgeProcess;
        private NamedPipeClientStream? _pipeClient;

        private readonly object _processLock = new();
        private readonly object _pipeLock = new();
        private bool _disposed;

        public LiquidctlBridgeWrapper(IPluginLogger logger)
        {
            _logger = logger;
        }

        public void Init()
        {
            EnsureBridgeProcessRunning();
            EnsurePipeConnection();
        }

        public void Shutdown() => Dispose();

        public void SetFixedSpeed(FixedSpeedRequest requestData)
        {
            var request = new PipeRequest
            {
                Command = "set.fixed_speed",
                Data = requestData
            };

            Task.Run(() => SendRequestAsync<object?>(request));
        }

        public IReadOnlyCollection<DeviceStatus> GetStatuses()
        {
            var request = new PipeRequest { Command = "get.statuses" };

            var result = Task.Run(() => SendRequestAsync<List<DeviceStatus>>(request)).GetAwaiter().GetResult();
            return new ReadOnlyCollection<DeviceStatus>(result ?? new List<DeviceStatus>());
        }

        private async Task<T?> SendRequestAsync<T>(PipeRequest request)
        {
            EnsurePipeConnection();

            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected) return default;

                byte[] payload = MessagePackSerializer.Serialize(request);

                await _pipeClient.WriteAsync(payload.AsMemory()).ConfigureAwait(false);
                await _pipeClient.FlushAsync().ConfigureAwait(false);

                byte[] buffer = new byte[65536];
                int bytesRead = await _pipeClient.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);

                if (bytesRead == 0) return default;

                var memory = new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
                var response = MessagePackSerializer.Deserialize<BridgeResponse<T>>(memory);

                if (response.Status == "error")
                {
                    _logger.Log($"[LiquidCtl] Bridge Error: {response.Error}");
                    return default;
                }

                return response.Data;
            }
            catch (IOException ex)
            {
                _logger.Log($"[LiquidCtl] Pipe IO Error: {ex.Message}");
                DisposePipe();
                return default;
            }
            catch (TimeoutException ex)
            {
                _logger.Log($"[LiquidCtl] Pipe Timeout: {ex.Message}");
                DisposePipe();
                return default;
            }
            catch (MessagePackSerializationException ex)
            {
                _logger.Log($"[LiquidCtl] Serialization Error: {ex.Message}");
                return default;
            }
        }

        private void EnsurePipeConnection()
        {
            lock (_pipeLock)
            {
                if (_pipeClient != null && _pipeClient.IsConnected) return;

                try
                {
                    DisposePipe();
                    _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                    _pipeClient.Connect(2000);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _pipeClient.ReadMode = PipeTransmissionMode.Message;
                    }
                }
                catch (IOException ex)
                {
                    _logger.Log($"[LiquidCtl] Pipe Connect Error: {ex.Message}");
                    DisposePipe();
                }
                catch (TimeoutException ex)
                {
                    _logger.Log($"[LiquidCtl] Pipe Connect Timeout: {ex.Message}");
                    DisposePipe();
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.Log($"[LiquidCtl] Pipe Access Denied: {ex.Message}");
                    DisposePipe();
                }
            }
        }

        private void EnsureBridgeProcessRunning()
        {
            lock (_processLock)
            {
                if (_bridgeProcess != null && !_bridgeProcess.HasExited) return;

                if (!File.Exists(_exePath))
                {
                    _logger.Log($"[LiquidCtl] Executable missing: {_exePath}");
                    return;
                }

                try
                {
                    _bridgeProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _exePath,
                            Arguments = "--log-level INFO",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    _bridgeProcess.Start();

                    _ = ReadStreamAsync(_bridgeProcess.StandardOutput);
                    _ = ReadStreamAsync(_bridgeProcess.StandardError);
                }
                catch (Win32Exception ex)
                {
                    _logger.Log($"[LiquidCtl] Process Start Error (Win32): {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    _logger.Log($"[LiquidCtl] Process Start Invalid Op: {ex.Message}");
                }
            }
        }

        private async Task ReadStreamAsync(StreamReader reader)
        {
            try
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Log($"[LiquidCtl Bridge] {line}");
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private void DisposePipe()
        {
             _pipeClient?.Dispose();
             _pipeClient = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                lock (_pipeLock)
                {
                    DisposePipe();
                }

                lock (_processLock)
                {
                    try
                    {
                        if (_bridgeProcess != null && !_bridgeProcess.HasExited)
                        {
                            _bridgeProcess.Kill();
                            _bridgeProcess.Dispose();
                        }
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }

                    _bridgeProcess = null;
                }
            }
            _disposed = true;
        }
    }
}
