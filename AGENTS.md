# FanControl.Liquidctl

FanControl plugin that bridges liquidctl (Python) into FanControl (Windows) to control and monitor AIO liquid coolers and smart devices.

## Tech Stack

- **C# Plugin:** .NET 10, IPlugin2 interface
- **Python Bridge:** Python 3.14+, uv, liquidctl
- **IPC:** Named Pipes with JSON serialization (msgspec on the Python side)

## Commands

```bash
# C# Plugin
dotnet build src/FanControl.Liquidctl

# C# Tests
dotnet test src/FanControl.Liquidctl.Tests

# Python Bridge
cd src/liquidctl_server && uv sync && uv build

# Tests
cd src/liquidctl_server && uv run pytest tests/

# Full Release
.\scripts\release.ps1
```

## File Structure

```
src/FanControl.Liquidctl/
├── LiquidctlPlugin.cs        # Main plugin (IPlugin2)
├── LiquidctlClient.cs        # Bridge client (domain logic)
├── BridgeProcess.cs          # Bridge exe lifecycle
├── PipeTransport.cs          # Framed pipe I/O, timeout, backoff
├── SensorMapper.cs           # DeviceStatus -> sensors
└── LiquidctlDevice.cs        # Sensor classes

src/liquidctl_server/
├── liquidctl_server/
│   ├── server.py             # Entry point, command handlers
│   ├── pipe_server.py        # Named-pipe server loop
│   ├── win32_pipe.py         # Win32 pipe layer
│   └── service/              # liquidctl service, executor, quirks, config
└── pyproject.toml
```

## Documentation

- [Architecture](agents/architecture.md) - Component structure, data models
- [Plugin Interface](agents/plugin-interface.md) - IPlugin2, sensor pairing, lifecycle
- [Protocol](agents/protocol.md) - Named pipes, commands, message format
- [Development](agents/development.md) - Error handling, performance, building
- [Troubleshooting](agents/troubleshooting.md) - Common issues and solutions

## References

- [FanControl Plugin SDK](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins)
- [liquidctl Documentation](https://github.com/liquidctl/liquidctl)
