# Audit: FanControl.LiquidCtl

Code audit of the C# plugin and Python bridge (2026-07-04).

## High — real bugs

### 1. No read timeout on the pipe → FanControl freeze

`LiquidctlClient.cs:154`

`_pipeClient.Read(...)` blocks forever if the bridge accepts the request but never replies (e.g. Python side stuck in a 5s+ device operation chain, or write-side failure where the server drops the response — `server.py:104-109` silently discards it). `GetStatuses()` runs inside FanControl's update loop under `_lock`, so a hung read freezes all sensor updates indefinitely. `BridgeConfig.RequestTimeoutMs = 5000` exists but is never used.

**Fix:** use async read with a timeout, or `CancellationTokenSource`.

### 2. Status cache never expires

`LiquidctlClient.cs:129`, `Models.cs:108`

`CachedStatuses.IsExpired` is never checked. When the bridge dies, `GetStatuses()` serves the last snapshot forever. FanControl then drives fan curves off a frozen temperature reading — a fan pinned at low speed on a stale 30 °C while the loop actually heats up. This is the one safety-relevant finding: stale sensors should go null so FanControl falls back to its failsafe behavior.

### 3. Bridge reports success on device failures

`liquidctl_service.py:258-261, 349-359`

`set_fixed_speed` and `set_color` catch all exceptions, log them, and return `None` → the pipe response is `status: success`. The C# side (and RGB clients) can never see a failed write. This directly caused issue #174's diagnosability pain.

**Fix:** let the exception propagate — `process_request` at `server.py:88` already converts it to an error response.

### 4. Sensor ID collision with two identical devices

`Utils.cs:37-40`, `LiquidctlPlugin.cs`

`CreateSensorId` uses only `device.Description + channel.Key`. Two units of the same model (two identical fan hubs — common) produce colliding IDs: `sensors[sensor.Id] = sensor` silently overwrites, both devices' controls end up steering one device. `DeviceStatus.Id` exists precisely for this — include it in the ID.

**Caveat:** changes IDs for existing users' saved configs, so gate it or only disambiguate on collision.

### 5. Empty device list treated as init failure

`LiquidctlClient.cs:61`

`TryInitialize` requires `GetStatuses().Any()`. On a machine with no liquidctl devices (or all filtered out), init kills and restarts the healthy bridge 3 times, sleeps ~24s total inside `Initialize()` (blocking FanControl startup), then marks `Faulted`. An empty list from a connected bridge is a valid outcome.

### 6. `set_number_of_devices` not idempotent on retry

`executor.py:57-66`, `liquidctl_service.py:73-89`

If `initialize_all` retries (`_find_devices` throws after the executor was created), a second `ThreadPoolExecutor` is created; the old one and its worker threads leak with live queues, and `_device_queues` entries are replaced while old workers still hold the old queues.

**Fix:** shut down the previous pool first, or make retries reuse it.

## Medium — design issues

### 7. Fire-and-forget `Set` with no ordering guarantee

`LiquidctlClient.cs:132-135`

Each `SetFixedSpeed` spawns an unbounded `Task.Run`. Tasks contend on `_lock` and there's no ordering: two quick duty changes can apply in reverse, leaving the fan at the stale value. A single-consumer queue (or at minimum a `SemaphoreSlim(1)` + last-write-wins coalescing) fixes both the ordering and the pile-up when the pipe is slow.

### 8. Duty dedup cache can block recovery

`liquidctl_service.py:247-248`

`previous_duty` skips re-sending an identical duty. If the device power-cycles or another tool (NZXT CAM) changes the duty, FanControl's periodic re-assert of the same value is dropped and the device stays wrong forever. Consider a TTL on the dedup, or drop it — the per-device queue already serializes writes.

### 9. Reconnect storm with 2s stalls when bridge is dead

`LiquidctlClient.cs:107, 143`

Once the bridge dies, every `GetStatuses()` (each FanControl tick) blocks up to `PipeConnectTimeoutMs = 2000` in `Connect()` under `_lock`. No backoff, no `Faulted` check, and the bridge process is never restarted after `Init()` (only `EnsurePipeConnected` is retried — `EnsureProcessStarted` isn't called from `SendRequest`). So a crashed bridge = permanent 2s/tick stall with no recovery.

**Fix:** either restart the process on reconnect or back off exponentially.

### 10. Fragile Win32 error handling

`pipe_server.py:11, 212-221`

`ctypes.windll.kernel32` without `use_last_error=True`, then `KERNEL32.GetLastError()` called after intervening Python work — the value can be clobbered by unrelated calls the interpreter makes. Disconnect detection in `_handle_client_session` (checking `GetLastError()` after `canread()` returned `False` for possibly a benign no-data reason) works by luck. Also `_wait_for_client` treating `ERROR_PIPE_BUSY` as "connected" (`pipe_server.py:215`) is wrong — that error is client-side; the server would enter a session on an unconnected pipe.

**Fix:** use `ctypes.WinDLL("kernel32", use_last_error=True)` + `ctypes.get_last_error()` captured immediately after each failing call.

### 11. No message-framing loop on the C# read

`LiquidctlClient.cs:153-154`

Single 64KB read with no `IsMessageComplete` check. A status payload > 64KB (many devices × many channels) or a byte-mode fragment turns into a `JsonException` and a dropped connection.

**Fix:** loop until `IsMessageComplete`.

### 12. Shutdown can leave the accept thread stuck

`pipe_server.py:207-215, 230-236`

`ConnectNamedPipe` blocks; the pending pipe handle `nph` isn't stored in `self.handle` until a client connects, so `close()` can't cancel it and `join(timeout=1.0)` just times out. Daemon threads mask it at process exit, but it means `close()` doesn't actually stop the server.

**Fix:** store `nph` before connecting, or use overlapped I/O with an event.

### 13. Busy-polling I/O design

`server.py:111`, `pipe_server.py:223-225`

Three threads sleeping 50–100ms in loops instead of blocking `ReadFile`. Adds up to ~150ms latency per request and constant wakeups in a background service meant to idle 24/7. Works, but blocking message-mode reads would delete `canread()`, `_handle_client_session()`, and both sleep loops.

## Low — dead code (delete it)

- **C#:** `ConnectionState` (`State` is written, never read by anyone; `Connecting`/`Connected` never assigned), `BridgeConfig.RequestTimeoutMs/HandshakeTimeoutMs/MaxRequestRetries/StatusCacheExpiryMs`, `CachedStatuses.IsExpired`, `CachedStatuses.Timestamp` — unless findings #1/#2 are fixed, which would resurrect them.
- **Python:** `Mode` enum with `MASTER`/`is_slave`/`is_master` (`models.py:66-78`) and `Base.mode` — the master half of a client/server abstraction that only has a server.
- `LiquidctlPlugin.Dispose(bool)` virtual-dispose pattern (`LiquidctlPlugin.cs:118-134`) on a class with no finalizer and no unmanaged state — `Close()` already disposes the client; a plain `Dispose` calling `liquidctl.Dispose()` suffices.

## What's in good shape

The per-device executor queue design is sound and the worker-survival fix (`executor.py:34-40`) is correct. `msgspec` typed decoding at the trust boundary, the `PipeRequest.data: Raw` handling, the device-filter file, and the driver-specific `_get_speed_channels` fallbacks are all pragmatic and well-commented. Test coverage is decent on both sides.

## Priority

If only three things get fixed: **#1** (read timeout), **#2** (cache expiry → null sensors), **#3** (propagate device errors) — together they turn "bridge misbehaves" from a silent freeze/stale-data hazard into visible, recoverable errors.
