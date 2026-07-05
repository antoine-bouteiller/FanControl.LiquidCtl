using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using FanControl.Plugins;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

internal sealed class FakeLogger : IPluginLogger
{
    public List<string> Messages { get; } = [];
    public void Log(string message) => Messages.Add(message);
}

[Collection("NamedPipe")]
public sealed class LiquidctlClientTests
{
    [WindowsOnlyFact]
    public void GetStatuses_WhenDisconnected_ReturnsEmpty()
    {
        using var client = new LiquidctlClient(new FakeLogger());
        Assert.Empty(client.GetStatuses());
    }

    [WindowsOnlyFact]
    public void Init_WithMissingBridgeExe_LogsMissingExe()
    {
        var logger = new FakeLogger();
        using var client = new LiquidctlClient(logger);

        client.Init();

        Assert.Contains(logger.Messages, m => m.Contains("Missing", StringComparison.Ordinal));
    }

    [WindowsOnlyFact]
    public void Dispose_IsIdempotent()
    {
        var client = new LiquidctlClient(new FakeLogger());

        client.Dispose();
        var ex = Record.Exception(client.Dispose);

        Assert.Null(ex);
    }

    [WindowsOnlyFact]
    public void SetFixedSpeed_WhenDisconnected_DoesNotThrow()
    {
        using var client = new LiquidctlClient(new FakeLogger());
        var request = new FixedSpeedRequest
        {
            DeviceId = 1,
            SpeedKwargs = new SpeedKwargs { Channel = "fan1", Duty = 50 }
        };

        var ex = Record.Exception(() => client.SetFixedSpeed(request));

        Assert.Null(ex);
    }

    // Must stay in this class: xUnit parallelizes across classes, which would
    // collide on the OS-global pipe name the client hardcodes.
    private const string PipeName = "LiquidCtlPipe";

    [SupportedOSPlatform("windows")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The server is owned and disposed by the returned worker thread via using.")]
    private static Thread StartOneShotServer(string responseJson, Action<string>? onRequest = null)
    {
        var server = new NamedPipeServerStream(
            PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

        var thread = new Thread(() =>
        {
            using (server)
            {
                server.WaitForConnection();
                var buffer = new byte[65536];
                int read = server.Read(buffer, 0, buffer.Length);
                onRequest?.Invoke(Encoding.UTF8.GetString(buffer, 0, read));

                byte[] response = Encoding.UTF8.GetBytes(responseJson);
                server.Write(response, 0, response.Length);
                server.Flush();
            }
        });
        thread.Start();
        return thread;
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void GetStatuses_WithRespondingServer_ReturnsParsedStatuses()
    {
        const string response = """
        {"status":"success","data":[{"id":1,"description":"Test Device","status":[{"key":"Fan 1 speed","value":1200,"unit":"rpm"}],"speed_channels":["fan1"]}]}
        """;
        var serverThread = StartOneShotServer(response);
        using var client = new LiquidctlClient(new FakeLogger());

        IReadOnlyList<DeviceStatus> result = client.GetStatuses();
        serverThread.Join(5000);

        var device = Assert.Single(result);
        Assert.Equal(1, device.Id);
        Assert.Equal("Test Device", device.Description);
        Assert.Equal(["fan1"], device.SpeedChannels);
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void GetStatuses_AfterSuccess_ReturnsCachedWhenRequestFails()
    {
        const string response = """
        {"status":"success","data":[{"id":1,"description":"Cached Device","status":[],"speed_channels":[]}]}
        """;
        var serverThread = StartOneShotServer(response);
        using var client = new LiquidctlClient(new FakeLogger());

        client.GetStatuses();
        serverThread.Join(5000);

        IReadOnlyList<DeviceStatus> cached = client.GetStatuses();

        Assert.Equal("Cached Device", Assert.Single(cached).Description);
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void SetFixedSpeed_WithServer_SendsSerializedRequest()
    {
        string? received = null;
        using var got = new ManualResetEventSlim(false);
        var serverThread = StartOneShotServer(
            """{"status":"success","data":null}""",
            request => { received = request; got.Set(); });

        using var client = new LiquidctlClient(new FakeLogger());
        client.SetFixedSpeed(new FixedSpeedRequest
        {
            DeviceId = 7,
            SpeedKwargs = new SpeedKwargs { Channel = "pump", Duty = 80 }
        });

        Assert.True(got.Wait(5000));
        serverThread.Join(5000);
        Assert.Contains("set.fixed_speed", received, StringComparison.Ordinal);
        Assert.Contains("pump", received, StringComparison.Ordinal);
        Assert.Contains("80", received, StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The server is owned and disposed by the returned worker thread via using.")]
    private static Thread StartMultiRequestServer(
        List<string> received, object receivedLock, int delayFirstRequestMs,
        string responseJson = """{"status":"success","data":null}""")
    {
        var server = new NamedPipeServerStream(
            PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

        var thread = new Thread(() =>
        {
            using (server)
            {
                server.WaitForConnection();
                var buffer = new byte[65536];
                bool first = true;
                while (true)
                {
                    int read;
                    try
                    {
                        read = server.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        return;
                    }
                    if (read == 0) return;

                    lock (receivedLock) received.Add(Encoding.UTF8.GetString(buffer, 0, read));

                    if (first)
                    {
                        first = false;
                        Thread.Sleep(delayFirstRequestMs);
                    }

                    byte[] response = Encoding.UTF8.GetBytes(responseJson);
                    try
                    {
                        server.Write(response, 0, response.Length);
                        server.Flush();
                    }
                    catch (IOException)
                    {
                        return;
                    }
                }
            }
        });
        thread.Start();
        return thread;
    }

    // Regression: DrainPendingSpeeds coalesces same-channel writes so a slow
    // server doesn't force one round trip per Set call - only the count and
    // the final value are guaranteed, not the exact number of round trips.
    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void SetFixedSpeed_RapidCallsForSameChannel_CoalescesToLastDuty()
    {
        var received = new List<string>();
        var receivedLock = new object();
        var serverThread = StartMultiRequestServer(received, receivedLock, delayFirstRequestMs: 300);

        using var client = new LiquidctlClient(new FakeLogger());
        for (int duty = 1; duty <= 10; duty++)
        {
            client.SetFixedSpeed(new FixedSpeedRequest
            {
                DeviceId = 3,
                SpeedKwargs = new SpeedKwargs { Channel = "fan1", Duty = duty }
            });
        }

        DateTime overallDeadlineUtc = DateTime.UtcNow.AddSeconds(10);
        int lastCount = -1;
        DateTime lastChangeUtc = DateTime.UtcNow;
        while (DateTime.UtcNow < overallDeadlineUtc)
        {
            int count;
            lock (receivedLock) count = received.Count;
            if (count != lastCount)
            {
                lastCount = count;
                lastChangeUtc = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - lastChangeUtc).TotalMilliseconds > 1000)
            {
                break;
            }
            Thread.Sleep(50);
        }

        client.Dispose();
        serverThread.Join(5000);

        List<string> snapshot;
        lock (receivedLock) snapshot = [.. received];

        Assert.True(snapshot.Count < 10, $"Expected coalescing to reduce request count below 10, got {snapshot.Count}");
        Assert.Contains("\"duty\":10", snapshot[^1], StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void GetStatuses_ServerReturnsError_LogsBridgeErrorAndReturnsEmpty()
    {
        const string response = """{"status":"error","data":null,"error":"boom"}""";
        var serverThread = StartOneShotServer(response);
        var logger = new FakeLogger();
        using var client = new LiquidctlClient(logger);

        IReadOnlyList<DeviceStatus> result = client.GetStatuses();
        serverThread.Join(5000);

        Assert.Empty(result);
        Assert.Contains(logger.Messages, m => m.Contains("Bridge error", StringComparison.Ordinal));
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public void GetStatuses_ServerReturnsInvalidJson_LogsInvalidResponseAndReturnsEmpty()
    {
        var serverThread = StartOneShotServer("not json");
        var logger = new FakeLogger();
        using var client = new LiquidctlClient(logger);

        IReadOnlyList<DeviceStatus> result = client.GetStatuses();
        serverThread.Join(5000);

        Assert.Empty(result);
        Assert.Contains(logger.Messages, m => m.Contains("Invalid response", StringComparison.Ordinal));
    }
}
