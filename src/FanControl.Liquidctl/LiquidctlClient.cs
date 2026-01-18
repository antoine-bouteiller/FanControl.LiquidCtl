using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FanControl.Plugins;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace FanControl.LiquidCtl
{
    public sealed class LiquidctlClient(IPluginLogger logger) : IDisposable
    {
        private readonly IPluginLogger _logger = logger;
        private readonly string _exePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "liquidctl_server.exe"
        );
        private const string PipeName = "LiquidCtlPipe";

        private Process? _bridgeProcess;
        private NamedPipeClientStream? _pipeClient;
        private readonly object _lock = new();

        private ConnectionState _state = ConnectionState.Disconnected;
        private CachedStatuses? _cachedStatuses;
        private CancellationTokenSource? _shutdownCts;
        private bool _disposed;

        public ConnectionState State
        {
            get { lock (_lock) return _state; }
            private set { lock (_lock) _state = value; }
        }

        public void Init()
        {
            _shutdownCts = new CancellationTokenSource();

            for (int attempt = 1; attempt <= BridgeConfig.MaxInitRetries; attempt++)
            {
                if (_shutdownCts.Token.IsCancellationRequested) return;

                _logger.Log($"[LiquidCtl] Initialization attempt {attempt}/{BridgeConfig.MaxInitRetries}");

                if (TryInitialize())
                {
                    State = ConnectionState.Ready;
                    _logger.Log("[LiquidCtl] Bridge initialized and ready");
                    return;
                }

                if (attempt < BridgeConfig.MaxInitRetries)
                {
                    _logger.Log($"[LiquidCtl] Initialization failed, retrying in {BridgeConfig.RetryDelayMs}ms...");
                    Thread.Sleep(BridgeConfig.RetryDelayMs);
                    Cleanup();
                }
            }

            State = ConnectionState.Faulted;
            _logger.Log("[LiquidCtl] Failed to initialize bridge after all retries");
        }

        public void Shutdown()
        {
            _shutdownCts?.Cancel();
            Dispose();
        }

        public void SetFixedSpeed(FixedSpeedRequest request)
        {
            if (_disposed || State != ConnectionState.Ready) return;

            _ = Task.Run(() =>
            {
                try
                {
                    SendRequestWithRetry<object>(
                        new PipeRequest { Command = "set.fixed_speed", Data = request },
                        retries: 2
                    );
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException ex) { _logger.Log($"[LiquidCtl] SetFixedSpeed: {ex.Message}"); }
                catch (IOException ex) { _logger.Log($"[LiquidCtl] SetFixedSpeed: {ex.Message}"); }
                catch (TimeoutException ex) { _logger.Log($"[LiquidCtl] SetFixedSpeed: {ex.Message}"); }
            });
        }

        public IReadOnlyList<DeviceStatus> GetStatuses()
        {
            if (_disposed) return [];

            try
            {
                var result = SendRequestWithRetry<List<DeviceStatus>>(
                    new PipeRequest { Command = "get.statuses" },
                    retries: BridgeConfig.MaxRequestRetries
                );

                if (result != null)
                {
                    _cachedStatuses = new CachedStatuses(result);
                    return result;
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException ex) { _logger.Log($"[LiquidCtl] GetStatuses: {ex.Message}"); }
            catch (IOException ex) { _logger.Log($"[LiquidCtl] GetStatuses: {ex.Message}"); }
            catch (TimeoutException ex) { _logger.Log($"[LiquidCtl] GetStatuses: {ex.Message}"); }

            if (_cachedStatuses != null)
            {
                _logger.Log("[LiquidCtl] Returning cached status");
                return _cachedStatuses.Statuses;
            }

            return [];
        }

        private bool TryInitialize()
        {
            State = ConnectionState.Connecting;

            if (!StartBridgeProcess())
                return false;

            Thread.Sleep(BridgeConfig.BridgeStartupDelayMs);

            if (!TryConnect())
                return false;

            State = ConnectionState.Connected;

            if (!VerifyHandshake())
            {
                _logger.Log("[LiquidCtl] Handshake failed");
                return false;
            }

            var statuses = SendRequest<List<DeviceStatus>>(new PipeRequest { Command = "get.statuses" });

            if (statuses == null || statuses.Count == 0)
            {
                _logger.Log("[LiquidCtl] No devices found");
                return false;
            }

            _cachedStatuses = new CachedStatuses(statuses);
            return true;
        }

        private bool VerifyHandshake()
        {
            try
            {
                var response = SendRequest<HandshakeResponse>(new PipeRequest { Command = "handshake" });
                return response?.Shake == true;
            }
            catch (OperationCanceledException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch (IOException) { return false; }
            catch (TimeoutException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private T? SendRequestWithRetry<T>(PipeRequest request, int retries) where T : class
        {
            for (int attempt = 1; attempt <= retries; attempt++)
            {
                _shutdownCts?.Token.ThrowIfCancellationRequested();

                var result = SendRequest<T>(request);
                if (result != null) return result;

                if (attempt < retries)
                {
                    DisconnectPipe();
                    Thread.Sleep(100);
                    TryConnect();
                }
            }
            return null;
        }

        private T? SendRequest<T>(PipeRequest request) where T : class
        {
            if (_disposed) return null;

            lock (_lock)
            {
                if (_disposed) return null;

                if (_pipeClient is not { IsConnected: true })
                {
                    if (!TryConnectInternal())
                        return null;
                }

                try
                {
                    byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));
                    _pipeClient!.Write(payload, 0, payload.Length);
                    _pipeClient.Flush();

                    byte[] buffer = new byte[65536];
                    int bytesRead = _pipeClient.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0) return null;

                    var response = JsonConvert.DeserializeObject<BridgeResponse<T>>(
                        Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    if (response == null) return null;

                    if (!response.IsSuccess)
                    {
                        _logger.Log($"[LiquidCtl] Bridge error: {response.Error}");
                        return null;
                    }

                    return response.Data;
                }
                catch (IOException ex)
                {
                    _logger.Log($"[LiquidCtl] IO error: {ex.Message}");
                    _pipeClient = DisposeAndNull(_pipeClient);
                    throw;
                }
                catch (TimeoutException ex)
                {
                    _logger.Log($"[LiquidCtl] Timeout: {ex.Message}");
                    _pipeClient = DisposeAndNull(_pipeClient);
                    throw;
                }
                catch (JsonException ex)
                {
                    _logger.Log($"[LiquidCtl] JSON error: {ex.Message}");
                    return null;
                }
            }
        }

        private bool TryConnect()
        {
            lock (_lock)
            {
                return TryConnectInternal();
            }
        }

        private bool TryConnectInternal()
        {
            if (_pipeClient is { IsConnected: true })
                return true;

            try
            {
                _pipeClient?.Dispose();
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                _pipeClient.Connect(BridgeConfig.PipeConnectTimeoutMs);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    _pipeClient.ReadMode = PipeTransmissionMode.Message;

                return true;
            }
            catch (IOException ex) { _logger.Log($"[LiquidCtl] Connect error: {ex.Message}"); }
            catch (TimeoutException ex) { _logger.Log($"[LiquidCtl] Connect timeout: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { _logger.Log($"[LiquidCtl] Access denied: {ex.Message}"); }

            _pipeClient = DisposeAndNull(_pipeClient);
            return false;
        }

        private static NamedPipeClientStream? DisposeAndNull(NamedPipeClientStream? client)
        {
            client?.Dispose();
            return null;
        }

        private void DisconnectPipe()
        {
            lock (_lock)
            {
                _pipeClient = DisposeAndNull(_pipeClient);
            }
        }

        private bool StartBridgeProcess()
        {
            lock (_lock)
            {
                if (_bridgeProcess is { HasExited: false }) return true;

                if (!File.Exists(_exePath))
                {
                    _logger.Log($"[LiquidCtl] Executable missing: {_exePath}");
                    return false;
                }

                KillExistingBridgeProcesses();

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

                    _bridgeProcess.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            _logger.Log($"[LiquidCtl Bridge] {e.Data}");
                    };
                    _bridgeProcess.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            _logger.Log($"[LiquidCtl Bridge] {e.Data}");
                    };

                    _bridgeProcess.Start();
                    _bridgeProcess.BeginOutputReadLine();
                    _bridgeProcess.BeginErrorReadLine();
                    return true;
                }
                catch (Win32Exception ex) { _logger.Log($"[LiquidCtl] Process start error: {ex.Message}"); }
                catch (InvalidOperationException ex) { _logger.Log($"[LiquidCtl] Process start error: {ex.Message}"); }

                return false;
            }
        }

        private void KillExistingBridgeProcesses()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("liquidctl"))
                {
                    try
                    {
                        _logger.Log($"[LiquidCtl] Killing existing bridge (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(BridgeConfig.ShutdownTimeoutMs);
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    finally { process.Dispose(); }
                }
            }
            catch (InvalidOperationException) { }
        }

        private void Cleanup()
        {
            DisconnectPipe();
            lock (_lock)
            {
                try
                {
                    if (_bridgeProcess is { HasExited: false })
                    {
                        _bridgeProcess.Kill();
                        _bridgeProcess.WaitForExit(BridgeConfig.ShutdownTimeoutMs);
                    }
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
                finally
                {
                    _bridgeProcess?.Dispose();
                    _bridgeProcess = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            State = ConnectionState.Disconnected;

            _shutdownCts?.Cancel();
            _shutdownCts?.Dispose();

            Cleanup();
            KillExistingBridgeProcesses();
        }
    }
}
