# Development Documentation

## Project Overview

FanControl.Liquidctl is a plugin for FanControl that bridges liquidctl functionality into the FanControl ecosystem, enabling comprehensive control and monitoring of AIO liquid coolers and smart devices.

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────┐
│                  FanControl.exe                     │
└────────────────┬────────────────────────────────────┘
                 │ IPlugin3 Interface
                 ▼
┌─────────────────────────────────────────────────────┐
│           FanControl.Liquidctl Plugin               │
│  ┌─────────────────────────────────────────────┐   │
│  │         LiquidctlPlugin.cs                   │   │
│  │  - Implements IPlugin3 interface             │   │
│  │  - Manages sensor/control registration       │   │
│  │  - Coordinates device discovery              │   │
│  └──────────────┬──────────────────────────────┘   │
│                 │                                    │
│  ┌──────────────▼──────────────────────────────┐   │
│  │     LiquidctlBridgeWrapper.cs               │   │
│  │  - Named Pipe client                         │   │
│  │  - JSON serialization/deserialization        │   │
│  │  - Process lifecycle management              │   │
│  └──────────────┬──────────────────────────────┘   │
└─────────────────┼──────────────────────────────────┘
                  │ Named Pipes (IPC)
                  ▼
┌─────────────────────────────────────────────────────┐
│           liquidctl_bridge (Python)                 │
│  - Wraps liquidctl library                          │
│  - Named Pipe server                                │
│  - Device initialization and status polling         │
│  - Device control operations                        │
└─────────────────┬───────────────────────────────────┘
                  │ USB/HID
                  ▼
┌─────────────────────────────────────────────────────┐
│              Hardware Devices                       │
│  - AIO liquid coolers (NZXT Kraken, etc.)          │
│  - Smart device controllers                         │
│  - RGB controllers                                  │
└─────────────────────────────────────────────────────┘
```

## Plugin Implementation

### IPlugin3 Interface

The plugin implements the `IPlugin3` interface, which is the latest plugin interface version for FanControl.

#### Interface Hierarchy

```csharp
IPlugin
├── string Name { get; }
├── void Initialize()
├── void Load(IPluginSensorsContainer container)
└── void Close()

IPlugin2 : IPlugin
└── void Update()

IPlugin3 : IPlugin2
└── event EventHandler? RefreshRequested
```

#### RefreshRequested Event

The `RefreshRequested` event is a mechanism to signal FanControl that the plugin needs:
- A complete refresh of all sensors and controls
- Recovery from a critical error or crash
- Reinitialization after device disconnection/reconnection

**Usage Pattern:**
```csharp
// Trigger refresh when a critical error occurs
try {
    // Device operation
} catch (Exception ex) {
    logger.Log($"Critical error: {ex.Message}");
    RefreshRequested?.Invoke(this, EventArgs.Empty);
}
```

### Lifecycle Methods

#### Initialize()
Called once when FanControl starts up. Use this to:
- Initialize the Python bridge process
- Establish Named Pipe connection
- Verify communication channel

#### Load(IPluginSensorsContainer container)
Called after Initialize(). Use this to:
- Discover connected devices
- Create sensor objects for each device channel
- Register sensors with FanControl via the container
- Set up control sensors for pump/fan speed adjustment

#### Update()
Called periodically by FanControl (typically every second). Use this to:
- Poll device status from the Python bridge
- Update sensor values
- Keep data in sync with hardware state

#### Close()
Called when FanControl shuts down. Use this to:
- Gracefully shutdown the Python bridge
- Clean up resources
- Close communication channels

## Core Classes

### LiquidctlPlugin

**Responsibility:** Main plugin entry point that coordinates all plugin operations.

**Key Features:**
- Implements IPlugin3 and IDisposable
- Maintains dictionary of active sensors for quick lookups
- Manages LiquidctlBridgeWrapper lifecycle
- Filters and registers supported sensor types (°C, rpm, %)

**Important Implementation Details:**
```csharp
// Only supported units are registered
List<string> supported_units = ["°C", "rpm", "%"];

// Control sensors (%) allow user adjustment
if (channel.Unit == "%") {
    ControlSensor sensor = new(device, channel, liquidctl);
    _container.ControlSensors.Add(sensor);
}

// Read-only sensors for monitoring
if (channel.Unit == "rpm") { _container.FanSensors.Add(sensor); }
if (channel.Unit == "°C") { _container.TempSensors.Add(sensor); }
```

### LiquidctlBridgeWrapper

**Responsibility:** Manages communication with the Python bridge process.

**Communication Protocol:**
- Uses Named Pipes for IPC (Inter-Process Communication)
- JSON serialization for message exchange
- Request/response pattern with timeout handling

**Key Methods:**
- `Init()`: Starts Python bridge and establishes connection
- `GetStatuses()`: Retrieves current device states
- `SetSpeed(device, channel, duty)`: Sets pump/fan speeds
- `Shutdown()`: Gracefully terminates bridge process

### DeviceSensor

**Responsibility:** Represents a read-only sensor (temperature or RPM).

**Key Features:**
- Implements IFanSensor or ITempSensor
- Stores device and channel information
- Updates value from status polling
- Generates unique ID for sensor identification

### ControlSensor

**Responsibility:** Represents a controllable output (pump/fan duty cycle).

**Key Features:**
- Extends DeviceSensor
- Implements IPluginControlSensor
- Allows setting values through FanControl UI
- Communicates changes back to hardware via bridge

## Development Setup

### Prerequisites

1. **.NET 8 SDK** - Required for building the C# plugin
2. **Python 3.8+** - Required for the bridge component
3. **Poetry** - Python dependency management
4. **Visual Studio 2022** or **VS Code** - Recommended IDEs
5. **FanControl** - For testing the plugin

### Repository Structure

```
FanControl.LiquidCtl/
├── src/
│   ├── FanControl.Liquidctl/          # C# Plugin
│   │   ├── LiquidctlPlugin.cs         # Main plugin class
│   │   ├── LiquidctlBridgeWrapper.cs  # IPC communication
│   │   ├── LiquidctlDevice.cs         # Sensor classes
│   │   ├── ref/                       # Referenced DLLs
│   │   │   ├── FanControl.Plugins.dll # FanControl plugin SDK
│   │   │   └── Newtonsoft.Json.dll    # JSON library
│   │   └── FanControl.Liquidctl.csproj
│   └── Liquidctl.Bridge/              # Python Bridge
│       ├── liquidctl_bridge/          # Bridge source
│       ├── tests/                     # Python tests
│       └── pyproject.toml             # Poetry config
├── docs/                              # Documentation & images
├── .github/                           # CI/CD workflows
├── build.ps1                          # Build script
├── README.md                          # User documentation
├── DEVELOPMENT.md                     # This file
└── CHANGELOG.md                       # Version history
```

### Building the Plugin

#### C# Plugin

```bash
cd src/FanControl.Liquidctl
dotnet build
```

Output: `bin/Debug/net8.0/FanControl.Liquidctl.dll`

#### Python Bridge

```bash
cd src/Liquidctl.Bridge
poetry install
poetry build
```

#### Complete Release Build

```powershell
# Build everything and create release package
.\build.ps1
```

### Testing

#### Manual Testing

1. Copy plugin DLL to FanControl plugins folder:
   ```powershell
   Copy-Item "src/FanControl.Liquidctl/bin/Debug/net8.0/*" `
             "C:\Program Files (x86)\FanControl\Plugins\FanControl.Liquidctl\" -Recurse
   ```

2. Launch FanControl
3. Check the log for plugin initialization
4. Verify sensors appear in the FanControl UI

#### Python Bridge Testing

```bash
cd src/Liquidctl.Bridge
poetry run python -m pytest tests/
```

### Debugging

#### C# Plugin Debugging

1. Set FanControl.exe as the startup program in VS project properties
2. Set breakpoints in plugin code
3. Press F5 to launch FanControl with debugger attached

#### Python Bridge Debugging

1. Run the bridge manually with logging:
   ```bash
   poetry run python liquidctl_bridge --log-level DEBUG
   ```
2. Monitor Named Pipe communication
3. Use the bridge test script to simulate requests

## Communication Protocol

### Named Pipes

**Pipe Name:** `liquidctl_bridge_<random>`

**Message Format:** JSON-RPC style

#### Initialize Request
```json
{
  "command": "initialize"
}
```

#### Get Status Request
```json
{
  "command": "get_status"
}
```

#### Get Status Response
```json
[
  {
    "description": "NZXT Kraken X63",
    "status": [
      {
        "key": "Liquid temperature",
        "value": 28.5,
        "unit": "°C"
      },
      {
        "key": "Pump speed",
        "value": 2500,
        "unit": "rpm"
      },
      {
        "key": "Pump duty",
        "value": 75,
        "unit": "%"
      }
    ]
  }
]
```

#### Set Speed Request
```json
{
  "command": "set_speed",
  "device": "NZXT Kraken X63",
  "channel": "pump",
  "duty": 80
}
```

### Error Handling

The bridge returns error responses in this format:
```json
{
  "error": "Device not found",
  "code": "DEVICE_NOT_FOUND"
}
```

## Best Practices

### Error Handling

1. **Always catch exceptions** in Update() to prevent FanControl crashes
2. **Use RefreshRequested** for recoverable errors
3. **Log errors** using IPluginLogger
4. **Graceful degradation** - continue working with available devices

### Performance

1. **Cache sensor objects** - don't recreate on every update
2. **Minimize bridge calls** - batch status requests
3. **Avoid blocking operations** in Update()
4. **Use timeout values** for pipe communication

### Compatibility

1. **Support all liquidctl devices** - don't hardcode device names
2. **Handle missing channels** - devices may not report all values
3. **Validate units** - only register supported unit types
4. **Version compatibility** - test with different FanControl versions

## Troubleshooting

### Common Issues

#### Plugin Not Loading
- Check FanControl logs for exceptions
- Verify all DLL dependencies are present
- Ensure .NET 8 runtime is available

#### Bridge Communication Failure
- Check if Python bridge process is running
- Verify Named Pipe connection is established
- Check bridge logs for errors

#### Device Not Detected
- Run liquidctl CLI directly to test device
- Check USB connection and drivers
- Verify device is supported by liquidctl

#### Performance Issues
- Reduce update frequency if needed
- Check for exceptions in Update() method
- Monitor bridge process CPU/memory usage

## Contributing

### Code Style

- Follow C# naming conventions
- Use modern C# features (records, pattern matching, etc.)
- Enable nullable reference types
- Treat warnings as errors

### Pull Request Process

1. Fork the repository
2. Create a feature branch
3. Make your changes with clear commits
4. Add tests for new functionality
5. Update documentation
6. Submit pull request with description

## References

- [FanControl Plugin Documentation](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins)
- [liquidctl Documentation](https://github.com/liquidctl/liquidctl)
- [Named Pipes Documentation](https://docs.microsoft.com/en-us/dotnet/standard/io/pipe-operations)
