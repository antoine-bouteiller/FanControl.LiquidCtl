using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FanControl.Plugins;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace FanControl.LiquidCtl
{
    public class LiquidctlBridgeWrapper : IDisposable
    {
        private readonly IPluginLogger _logger;
        private static readonly string _exePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "liquidctl_bridge.exe"
        );
        private const string _pipeName = "LiquidCtlPipe";

        private Process? _bridgeProcess;
        private NamedPipeClientStream? _pipeClient;

        private readonly object _processLock = new();
        private readonly object _pipeLock = new();
        private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
        private bool _disposed;

        public LiquidctlBridgeWrapper(IPluginLogger logger)
        {
            _logger = logger;
        }

        public void Init()
        {
            EnsureBridgeProcessRunning();

            for (int attempt = 0; attempt < BridgeConfig.MaxConnectRetries; attempt++)
            {
                EnsurePipeConnection();
                if (_pipeClient is { IsConnected: true })
                {
                    return;
                }

                if (attempt < BridgeConfig.MaxConnectRetries - 1)
                {
                    _logger.Log($"[LiquidCtl] Connection attempt {attempt + 1} failed, retrying...");
                    Thread.Sleep(BridgeConfig.RetryDelayMs);
                }
            }

            _logger.Log($"[LiquidCtl] Failed to connect after {BridgeConfig.MaxConnectRetries} attempts");
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
            if (!await _requestSemaphore.WaitAsync(BridgeConfig.RequestTimeoutMs).ConfigureAwait(false))
            {
                _logger.Log("[LiquidCtl] Request timeout waiting for semaphore");
                return default;
            }

            try
            {
                EnsurePipeConnection();

                if (_pipeClient is not { IsConnected: true })
                {
                    return default;
                }

                using var cts = new CancellationTokenSource(BridgeConfig.RequestTimeoutMs);

                string jsonPayload = JsonConvert.SerializeObject(request);
                byte[] payload = Encoding.UTF8.GetBytes(jsonPayload);
                await _pipeClient.WriteAsync(payload.AsMemory(), cts.Token).ConfigureAwait(false);
                await _pipeClient.FlushAsync(cts.Token).ConfigureAwait(false);

                byte[] buffer = new byte[65536];
                int bytesRead = await _pipeClient.ReadAsync(buffer.AsMemory(), cts.Token).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    return default;
                }

                string jsonResponse = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = JsonConvert.DeserializeObject<BridgeResponse<T>>(jsonResponse);

                if (response == null)
                {
                    _logger.Log("[LiquidCtl] Failed to deserialize response");
                    return default;
                }

                if (response.Status == "error")
                {
                    _logger.Log($"[LiquidCtl] Bridge Error: {response.Error}");
                    return default;
                }

                return response.Data;
            }
            catch (OperationCanceledException)
            {
                _logger.Log("[LiquidCtl] Request timed out");
                DisposePipe();
            }
            catch (IOException ex)
            {
                _logger.Log($"[LiquidCtl] Pipe IO Error: {ex.Message}");
                DisposePipe();
            }
            catch (TimeoutException ex)
            {
                _logger.Log($"[LiquidCtl] Pipe Timeout: {ex.Message}");
                DisposePipe();
            }
            catch (JsonException ex)
            {
                _logger.Log($"[LiquidCtl] JSON Serialization Error: {ex.Message}");
            }
            finally
            {
                _requestSemaphore.Release();
            }

            return default;
        }

        private void EnsurePipeConnection()
        {
            lock (_pipeLock)
            {
                if (_pipeClient is { IsConnected: true })
                {
                    return;
                }

                try
                {
                    DisposePipe();
                    _pipeClient = new NamedPipeClientStream(
                        ".",
                        _pipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );

                    _pipeClient.Connect(BridgeConfig.PipeConnectTimeoutMs);

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
                            Arguments = "--log-level ERROR",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    _bridgeProcess.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            _logger.Log($"[LiquidCtl Bridge] {e.Data}");
                    };
                    _bridgeProcess.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            _logger.Log($"[LiquidCtl Bridge] {e.Data}");
                    };

                    _bridgeProcess.Start();
                    _bridgeProcess.BeginOutputReadLine();
                    _bridgeProcess.BeginErrorReadLine();
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
                        if (_bridgeProcess is { HasExited: false })
                        {
                            _bridgeProcess.Kill();
                            _bridgeProcess.Dispose();
                        }
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }

                    _bridgeProcess = null;
                }

                _requestSemaphore.Dispose();
            }

            _disposed = true;
        }
    }
}
