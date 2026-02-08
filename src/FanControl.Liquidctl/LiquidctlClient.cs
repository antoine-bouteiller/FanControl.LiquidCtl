using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public sealed class LiquidctlClient(IPluginLogger logger) : IDisposable
    {
        private readonly IPluginLogger _logger = logger;
        private readonly string _exePath = Path.Combine(Path.GetDirectoryName(typeof(LiquidctlClient).Assembly.Location) ?? string.Empty, "liquidctl_server.exe");
        private const string PipeName = "LiquidCtlPipe";

        private Process? _bridgeProcess;
        private NamedPipeClientStream? _pipeClient;
        private readonly object _lock = new();

        private ConnectionState _state = ConnectionState.Disconnected;
        private CachedStatuses? _cachedStatuses;
        private bool _disposed;

        public ConnectionState State
        {
            get { lock (_lock) return _state; }
            private set { lock (_lock) _state = value; }
        }

        public void Init()
        {
            for (int attempt = 1; attempt <= BridgeConfig.MaxInitRetries; attempt++)
            {
                if (TryInitialize())
                {
                    State = ConnectionState.Ready;
                    return;
                }

                _logger.Log($"[LiquidCtl] Failed, retrying in {BridgeConfig.RetryDelayMs}ms...");
                Cleanup();
                Thread.Sleep(BridgeConfig.RetryDelayMs);
            }

            State = ConnectionState.Faulted;
        }

        private bool TryInitialize()
        {
            if (!EnsureProcessStarted()) return false;

            Thread.Sleep(BridgeConfig.BridgeStartupDelayMs);

            return EnsurePipeConnected() && GetStatuses().Any();
        }

        private bool EnsureProcessStarted()
        {
            lock (_lock)
            {
                if (_bridgeProcess is { HasExited: false }) return true;

                if (!File.Exists(_exePath))
                {
                    _logger.Log($"[LiquidCtl] Missing: {_exePath}");
                    return false;
                }

                KillExistingBridgeProcesses();

                _bridgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo(_exePath, "--log-level ERROR")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                _bridgeProcess.OutputDataReceived += (_, e) => { if (e.Data != null) _logger.Log($"[LiquidCtl Bridge] {e.Data}"); };
                _bridgeProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) _logger.Log($"[LiquidCtl Bridge] {e.Data}"); };

                _bridgeProcess.Start();
                _bridgeProcess.BeginOutputReadLine();
                _bridgeProcess.BeginErrorReadLine();
                return true;
            }
        }

        private bool EnsurePipeConnected()
        {
            if (_pipeClient is { IsConnected: true }) return true;

            try
            {
                _pipeClient?.Dispose();
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                _pipeClient.Connect(BridgeConfig.PipeConnectTimeoutMs);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    _pipeClient.ReadMode = PipeTransmissionMode.Message;

                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                _logger.Log($"[LiquidCtl] Pipe connection failed: {ex.Message}");
                return false;
            }
        }

        public IReadOnlyList<DeviceStatus> GetStatuses()
        {
            var result = SendRequest<List<DeviceStatus>>(new PipeRequest { Command = "get.statuses" });
            if (result != null)
            {
                _cachedStatuses = new CachedStatuses(result);
                return result;
            }
            return _cachedStatuses?.Statuses ?? new List<DeviceStatus>();
        }

        public void SetFixedSpeed(FixedSpeedRequest request)
        {
            Task.Run(() => SendRequest<object>(new PipeRequest { Command = "set.fixed_speed", Data = request }));
        }

        private T? SendRequest<T>(PipeRequest request) where T : class
        {
            if (_disposed) return null;

            lock (_lock)
            {
                if (!EnsurePipeConnected()) return null;

                try
                {
                    string json = JsonSerializer.Serialize(request);
                    byte[] payload = Encoding.UTF8.GetBytes(json);

                    _pipeClient!.Write(payload, 0, payload.Length);
                    _pipeClient.Flush();

                    byte[] buffer = new byte[65536];
                    int bytesRead = _pipeClient.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0) return null;

                    var response = JsonSerializer.Deserialize<ServerResponse<T>>(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    if (response?.IsSuccess == true) return response.Data;

                    _logger.Log($"[LiquidCtl] Bridge error: {response?.Error}");
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or JsonException)
                {
                    _logger.Log($"[LiquidCtl] Request failed: {ex.Message}");
                    _pipeClient?.Dispose();
                    _pipeClient = null;
                }
                return null;
            }
        }

        private static void KillExistingBridgeProcesses()
        {
            foreach (var p in Process.GetProcessesByName("liquidctl_server"))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { } finally { p.Dispose(); }
            }
        }

        private void Cleanup()
        {
            lock (_lock)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;

                if (_bridgeProcess is { HasExited: false })
                {
                    try { _bridgeProcess.Kill(); _bridgeProcess.WaitForExit(BridgeConfig.ShutdownTimeoutMs); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                }
                _bridgeProcess?.Dispose();
                _bridgeProcess = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
            KillExistingBridgeProcesses();
        }
    }
}
