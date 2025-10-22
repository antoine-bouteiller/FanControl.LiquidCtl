using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
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

    public class LiquidctlBridgeWrapper(IPluginLogger logger) : IDisposable
    {
        private static readonly string liquidctlexe = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "liquidctl_bridge.exe");
        private const string pipeName = "LiquidCtlPipe";
        private static Process? bridgeProcess;
        private static readonly object processLock = new();
        private NamedPipeClientStream? _pipeClient;
        private readonly object _pipeLock = new();
        private bool _disposed;


        private void EnsureBridgeProcessRunning()
        {
            lock (processLock)
            {
                if (bridgeProcess == null || bridgeProcess.HasExited)
                {
                    bridgeProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = liquidctlexe,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    // Attach event handlers BEFORE starting the process to avoid buffer overflow
                    bridgeProcess.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            logger.Log($"[FanControl.LiquidCtl] {args.Data}");
                        }
                    };
                    bridgeProcess.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            logger.Log($"[FanControl.LiquidCtl] {args.Data}");
                        }
                    };

                    _ = bridgeProcess.Start();

                    // Start reading output immediately to prevent buffer deadlock
                    bridgeProcess.BeginOutputReadLine();
                    bridgeProcess.BeginErrorReadLine();

                    // Give the Python process time to initialize and create the named pipe
                    // The Python server needs to start its thread and create the pipe
                    System.Threading.Thread.Sleep(2000);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_pipeLock)
                    {
                        _pipeClient?.Dispose();
                        _pipeClient = null;
                    }

                    lock (processLock)
                    {
                        if (bridgeProcess != null && !bridgeProcess.HasExited)
                        {
                            bridgeProcess.Kill();
                            _ = bridgeProcess.WaitForExit(1000);
                            bridgeProcess.Dispose();
                            bridgeProcess = null;
                        }
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void EnsurePipeConnection()
        {
            lock (_pipeLock)
            {
                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    const int maxRetries = 3;
                    int retryCount = 0;
                    Exception? lastException = null;

                    while (retryCount < maxRetries)
                    {
                        NamedPipeClientStream? tempClient = null;
                        try
                        {
                            _pipeClient?.Dispose();
                            tempClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                            tempClient.Connect(5000);
                            _pipeClient = tempClient;
                            logger.Log("Successfully connected to liquidctl named pipe");
                            return;
                        }
                        catch (TimeoutException ex)
                        {
                            lastException = ex;
                            retryCount++;
                            logger.Log($"Pipe connection attempt {retryCount}/{maxRetries} timed out");

                            // tempClient is guaranteed to be non-null here since Connect() was called on it
                            ArgumentNullException.ThrowIfNull(tempClient);
                            tempClient.Dispose();
                            _pipeClient = null;

                            if (retryCount < maxRetries)
                            {
                                System.Threading.Thread.Sleep(1000);
                            }
                        }
                        catch (Exception ex)
                        {
                            tempClient?.Dispose();
                            _pipeClient = null;
                            throw new IOException($"Error connecting to Named Pipe: {ex.Message}", ex);
                        }
                    }

                    throw new IOException($"Failed to connect to Named Pipe after {maxRetries} attempts: {lastException?.Message}", lastException);
                }
            }
        }

        public void Init()
        {
            EnsureBridgeProcessRunning();
            EnsurePipeConnection();
        }

        private string SendPipeRequest(PipeRequest request)
        {
            EnsureBridgeProcessRunning();
            EnsurePipeConnection();

            lock (_pipeLock)
            {
                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    throw new IOException("Unable to establish connection to liquidctl server");
                }

                try
                {
                    logger.Log($"Sending command: {request.Command}");
                    string requestJson = JsonConvert.SerializeObject(request);
                    byte[] requestData = Encoding.UTF8.GetBytes(requestJson);

                    _pipeClient.Write(requestData, 0, requestData.Length);
                    _pipeClient.Flush();

                    byte[] buffer = new byte[4096];
                    int bytesRead = _pipeClient.Read(buffer, 0, buffer.Length);
                    logger.Log($"Received {bytesRead} bytes from pipe");
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch (IOException ex)
                {
                    _pipeClient.Dispose();
                    _pipeClient = null;
                    throw new IOException($"Communication error with liquidctl server: {ex.Message}", ex);
                }
            }
        }

        public IReadOnlyCollection<DeviceStatus> GetStatuses()
        {
            PipeRequest request = new()
            {
                Command = "get.statuses"
            };

            try
            {
                string response = SendPipeRequest(request);
                return JsonConvert.DeserializeObject<List<DeviceStatus>>(response) ?? [];
            }
            catch (JsonException ex)
            {
                logger.Log($"Error deserializing statuses: {ex.Message}");
                return [];
            }
            catch (IOException ex)
            {
                logger.Log($"IO error retrieving statuses: {ex.Message}");
                return [];
            }
        }

        public void SetFixedSpeed(FixedSpeedRequest requestData)
        {
            PipeRequest request = new()
            {
                Command = "set.fixed_speed",
                Data = requestData
            };

            _ = SendPipeRequest(request);
        }

        public void Shutdown()
        {
            lock (_pipeLock)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;
            }

            lock (processLock)
            {
                if (bridgeProcess != null && !bridgeProcess.HasExited)
                {
                    bridgeProcess.Kill();
                    bridgeProcess.Dispose();
                    bridgeProcess = null;
                }
            }
        }
    }
}
