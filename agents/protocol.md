# Communication Protocol

## Named pipes

The bridge serves two Windows named pipes:

- `\\.\pipe\LiquidCtlPipe` — primary pipe (sensors + fan/pump control). The
  FanControl plugin holds this connection open.
- `\\.\pipe\LiquidCtlPipeRgb` — dedicated RGB pipe. The pipe server is
  single-client per instance and the fan client keeps the primary pipe busy, so
  RGB callers connect here. Both pipes feed the same service, so every command
  runs on the same per-device serialized HID queue.

Under `--test`, the names gain a `Test` suffix (`LiquidCtlPipeTest`,
`LiquidCtlPipeTestRgb`).

**Framing:** message-mode pipe, one UTF-8 JSON object per message (encoded/decoded
with `msgspec`). There is no length prefix.

## Request envelope

```json
{ "command": "<name>", "data": { /* command-specific, or null */ } }
```

`data` is decoded per command; omit it or send `null` when a command takes no
payload.

## Response envelope

```json
{ "status": "success", "data": <result-or-null>, "error": null }
```

On failure: `{ "status": "error", "data": null, "error": "<message>" }`.

## Commands

### `get.statuses`

No `data`. Returns one entry per connected device.

```json
{ "command": "get.statuses" }
```

Response `data`:

```json
[
  {
    "id": 1,
    "description": "NZXT Kraken X (X53, X63 or X73)",
    "status": [
      { "key": "Liquid temperature", "value": 28.5, "unit": "°C" },
      { "key": "Pump speed",         "value": 2500, "unit": "rpm" },
      { "key": "Pump duty",          "value": 75,   "unit": "%" }
    ]
  }
]
```

### `set.fixed_speed`

Sets a fixed duty on a channel. `device_id` is the 1-based index from
`get.statuses`.

```json
{
  "command": "set.fixed_speed",
  "data": { "device_id": 1, "speed_kwargs": { "channel": "pump", "duty": 80 } }
}
```

Response `data` is `null`.

### `set.led`

Applies per-LED colours to a lighting channel. The device is matched by a
case-insensitive substring of its description (callers target by name, not by
id). `colors` is one `[r, g, b]` triple (0–255) per LED on the channel.

```json
{
  "command": "set.led",
  "data": {
    "device": "Kraken",
    "channel": "ring",
    "mode": "super-fixed",
    "colors": [[255, 0, 0], [0, 255, 0], [0, 0, 255]]
  }
}
```

Response `data` is `null`. Channel names and LED counts are device-specific (e.g.
Kraken `ring`/`logo`, Smart Device `led1`/`led2`); `mode` is any liquidctl colour
mode the channel supports (`super-fixed` for a per-LED frame).

## Diagnostics

Set the `LIQUIDCTL_BRIDGE_LOG` environment variable (e.g. `INFO`, `DEBUG`) to
override the log level without changing the host plugin's spawn arguments. At
startup the bridge logs a device inventory (descriptions, drivers, colour
channels, LED counts); `set.led` requests are traced with the resolved device,
channel, mode, and colour count.
