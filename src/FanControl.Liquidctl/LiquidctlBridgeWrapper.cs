using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    // Classes pour la communication avec le serveur Named Pipe
    public class PipeRequest
    {
        public required string command { get; set; }

        public FixedSpeedRequest? data { get; set; }
    }
    public class SpeedKwargs
    {
        public required string channel { get; set; }
        public required int duty { get; set; }
    }

    public class FixedSpeedRequest
    {
        public required int device_id { get; set; }

        public required SpeedKwargs speed_kwargs { get; set; }
    }

    public class LiquidctlBridgeWrapper(IPluginLogger logger)
    {
        private static readonly string liquidctlexe = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "liquidctl_bridge.exe");
        private static readonly string pipeName = "LiquidCtlPipe";
        private static Process? bridgeProcess;
        private static readonly object processLock = new object();
        private readonly IPluginLogger _logger = logger;

        private NamedPipeClientStream? _pipeClient;
        private readonly object _pipeLock = new object();

        private static void EnsureBridgeProcessRunning()
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

                    bridgeProcess.Start();
                }
            }
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
                        _logger.Log($"Erreur lors de la connexion au Named Pipe: {ex.Message}");
                        _pipeClient?.Dispose();
                        _pipeClient = null;
                        throw;
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
                    throw new Exception("Impossible d'établir une connexion au serveur liquidctl");
                }

                try
                {
                    string requestJson = JsonConvert.SerializeObject(request);
                    byte[] requestData = Encoding.UTF8.GetBytes(requestJson);

                    _pipeClient.Write(requestData, 0, requestData.Length);
                    _pipeClient.Flush();

                    byte[] buffer = new byte[4096];
                    int bytesRead = _pipeClient.Read(buffer, 0, buffer.Length);
                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Erreur de communication avec le serveur liquidctl: {ex.Message}");
                    _pipeClient?.Dispose();
                    _pipeClient = null;
                    throw new Exception($"Erreur de communication avec le serveur liquidctl: {ex.Message}", ex);
                }
            }
        }

        public List<DeviceStatus> GetStatuses()
        {
            var request = new PipeRequest
            {
                command = "get.statuses"
            };

            try
            {
                var response = SendPipeRequest(request);
                return JsonConvert.DeserializeObject<List<DeviceStatus>>(response) ?? [];
            }
            catch (Exception ex)
            {
                _logger.Log($"Erreur lors de la récupération des statuses: {ex.Message}");
                return [];
            }
        }

        public void SetFixedSpeed(FixedSpeedRequest requestData)
        {
            var request = new PipeRequest
            {
                command = "set.fixed_speed",
                data = requestData
            };

            SendPipeRequest(request);
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