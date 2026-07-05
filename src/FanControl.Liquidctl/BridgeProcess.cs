using System.ComponentModel;
using System.Diagnostics;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    internal sealed class BridgeProcess(IPluginLogger logger) : IDisposable
    {
        private readonly IPluginLogger _logger = logger;
        private readonly string _exePath = ResolveExePath();
        private readonly object _lock = new();
        private Process? _process;
        private bool _missingExeLogged;
        private bool _disposed;

        private static string ResolveExePath()
        {
            var baseDir = Path.GetDirectoryName(typeof(BridgeProcess).Assembly.Location) ?? string.Empty;
            return Path.Combine(baseDir, "liquidctl_server", "liquidctl_server.exe");
        }

        public bool EnsureRunning()
        {
            lock (_lock)
            {
                if (_disposed) return false;

                if (_process is { HasExited: false }) return true;

                if (!File.Exists(_exePath))
                {
                    if (!_missingExeLogged) _logger.Log($"[LiquidCtl] Missing: {_exePath}");
                    _missingExeLogged = true;
                    return false;
                }

                KillStrayBridgeProcesses();

                _process?.Dispose();
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo(_exePath, "--log-level ERROR")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                _process.OutputDataReceived += (_, e) => { if (e.Data != null) _logger.Log($"[LiquidCtl Bridge] {e.Data}"); };
                _process.ErrorDataReceived += (_, e) => { if (e.Data != null) _logger.Log($"[LiquidCtl Bridge] {e.Data}"); };

                try
                {
                    _process.Start();
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    _logger.Log($"[LiquidCtl] Failed to start bridge: {ex.Message}");
                    _process.Dispose();
                    _process = null;
                    return false;
                }

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                return true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_process is { HasExited: false })
                {
                    try { _process.Kill(); _process.WaitForExit(BridgeConfig.ShutdownTimeoutMs); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* Expected when process already exited */ }
                }
                _process?.Dispose();
                _process = null;
            }
            KillStrayBridgeProcesses();
        }

        private static void KillStrayBridgeProcesses()
        {
            foreach (var p in Process.GetProcessesByName("liquidctl_server"))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* Expected when process already exited */ } finally { p.Dispose(); }
            }
        }

        public void Dispose()
        {
            // Mark disposed before stopping so a concurrent EnsureRunning call
            // cannot restart the bridge right after it is killed.
            lock (_lock) { _disposed = true; }
            Stop();
        }
    }
}
