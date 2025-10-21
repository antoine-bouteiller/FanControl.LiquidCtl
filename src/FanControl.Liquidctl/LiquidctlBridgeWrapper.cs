using System.Text.Json.Serialization;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    public class PipeRequest
    {
        [JsonPropertyName("command")]
        public required string Command { get; set; }

        [JsonPropertyName("data")]
        public FixedSpeedRequest? Data { get; set; }
    }

    public class SpeedKwargs
    {
        [JsonPropertyName("channel")]
        public required string Channel { get; set; }

        [JsonPropertyName("duty")]
        public required int Duty { get; set; }
    }

    public class FixedSpeedRequest
    {
        [JsonPropertyName("device_id")]
        public required int DeviceId { get; set; }

        [JsonPropertyName("speed_kwargs")]
        public required SpeedKwargs SpeedKwargs { get; set; }
    }

    public class LiquidctlBridgeWrapper(IPluginLogger logger) : IDisposable
    {
        private static readonly string liquidctlexe = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "liquidctl_bridge.exe");
        private static readonly string pipeName = "LiquidCtlPipe";
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
                    bridgeProcess.BeginOutputReadLine();
                    bridgeProcess.BeginErrorReadLine();
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
                    try
                    {
                        _pipeClient?.Dispose();
                        _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                        _pipeClient.Connect(5000);
                    }
                    catch (Exception ex)
                    {
                        _pipeClient?.Dispose();
                        _pipeClient = null;
                        throw new IOException($"Error connecting to Named Pipe: {ex.Message}", ex);
                    }
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

        public List<DeviceStatus> GetStatuses()
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
