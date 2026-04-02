# FanControl.Liquidctl

FanControl plugin that bridges liquidctl (Python) into FanControl (Windows) to control and monitor AIO liquid coolers and smart devices.

## Tech Stack

- **C# Plugin:** .NET 10, IPlugin3 interface
- **Python Bridge:** Python 3.11+, uv, liquidctl
- **IPC:** Named Pipes with MessagePack serialization

## Commands

```bash
# C# Plugin
dotnet build src/FanControl.Liquidctl

# Python Bridge
cd src/Liquidctl && uv sync && uv build

# Tests
cd src/Liquidctl && uv run pytest tests/

# Full Release
.\scripts\release.ps1
```

## File Structure

```
src/FanControl.Liquidctl/
├── LiquidctlPlugin.cs        # Main plugin (IPlugin3)
├── LiquidctlBridgeWrapper.cs # IPC client
└── LiquidctlDevice.cs        # Sensor classes

src/Liquidctl/
├── liquidctl/
│   ├── __main__.py           # Entry point
│   └── bridge.py             # Bridge logic
└── pyproject.toml
```

## Documentation

- [Architecture](agents/architecture.md) - Component structure, data models
- [Plugin Interface](agents/plugin-interface.md) - IPlugin3, sensor pairing, lifecycle
- [Protocol](agents/protocol.md) - Named pipes, commands, message format
- [Development](agents/development.md) - Error handling, performance, building
- [Troubleshooting](agents/troubleshooting.md) - Common issues and solutions

## References

- [FanControl Plugin SDK](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins)
- [liquidctl Documentation](https://github.com/liquidctl/liquidctl)
