using System.IO.Pipes;
using FanControl.Plugins;

namespace FanControl.LiquidCtl
{
    internal sealed class PipeTransport(IPluginLogger logger) : IDisposable
    {
        private const string PipeName = "LiquidCtlPipe";

        private readonly IPluginLogger _logger = logger;
        private readonly object _lock = new();
        private NamedPipeClientStream? _pipe;
        private bool _messageMode;
        private DateTime _nextConnectAttemptUtc = DateTime.MinValue;
        private bool _disposed;

        public byte[]? Request(byte[] payload)
        {
            lock (_lock)
            {
                if (_disposed || !EnsureConnected()) return null;

                try
                {
                    using var cts = new CancellationTokenSource(BridgeConfig.RequestTimeoutMs);
                    _pipe!.Write(payload, 0, payload.Length);
                    _pipe.Flush();
                    return ReadMessage(cts.Token);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
                {
                    _logger.Log($"[LiquidCtl] Request failed: {ex.Message}");
                    Disconnect();
                    return null;
                }
            }
        }

        public void ResetBackoff()
        {
            lock (_lock) _nextConnectAttemptUtc = DateTime.MinValue;
        }

        private byte[] ReadMessage(CancellationToken cancellationToken)
        {
            var buffer = new byte[65536];
            using var message = new MemoryStream();
            do
            {
                int bytesRead = _pipe!.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask().GetAwaiter().GetResult();
                if (bytesRead == 0) throw new IOException("Pipe closed by server");
                message.Write(buffer, 0, bytesRead);
            } while (_messageMode && !_pipe.IsMessageComplete);
            return message.ToArray();
        }

        private bool EnsureConnected()
        {
            if (_pipe is { IsConnected: true }) return true;
            if (DateTime.UtcNow < _nextConnectAttemptUtc) return false;

            try
            {
                _pipe?.Dispose();
                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                _pipe.Connect(BridgeConfig.PipeConnectTimeoutMs);

                _messageMode = OperatingSystem.IsWindows();
                if (OperatingSystem.IsWindows()) _pipe.ReadMode = PipeTransmissionMode.Message;

                _nextConnectAttemptUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                _logger.Log($"[LiquidCtl] Pipe connection failed: {ex.Message}");
                _nextConnectAttemptUtc = DateTime.UtcNow.AddMilliseconds(BridgeConfig.ReconnectBackoffMs);
                Disconnect();
                return false;
            }
        }

        private void Disconnect()
        {
            _pipe?.Dispose();
            _pipe = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                Disconnect();
            }
        }
    }
}
