# CLAUDE.md - Technical Documentation for AI Assistance

This file contains important technical information about the FanControl.Liquidctl project for AI-assisted development.

## Project Overview

FanControl.Liquidctl is a FanControl plugin that bridges liquidctl (Python library) functionality into FanControl (Windows application) to control and monitor AIO liquid coolers and smart devices.

## Architecture

### Component Structure

```
FanControl.exe (Host Application)
    ↓ IPlugin3 Interface
FanControl.Liquidctl Plugin (.NET 8)
    ├── LiquidctlPlugin.cs (Main plugin, implements IPlugin3)
    ├── LiquidctlBridgeWrapper.cs (IPC client)
    └── LiquidctlDevice.cs (Sensor/control classes)
    ↓ Named Pipes (IPC)
liquidctl (Python executable)
    ↓ USB/HID
Hardware Devices (AIO coolers, smart devices)
```

### Key Technologies

- **.NET 8** (C# plugin)
- **Python 3.8+** with uv (bridge executable)
- **Named Pipes** (inter-process communication)
- **MessagePack** (message serialization)
- **liquidctl** (Python library for hardware control)

## Plugin Implementation

### IPlugin3 Interface

The plugin implements `IPlugin3`, which is the latest FanControl plugin interface:

```csharp
IPlugin3 : IPlugin2
    └── event EventHandler? RefreshRequested

IPlugin2 : IPlugin
    └── void Update()

IPlugin : base interface
    ├── string Name { get; }
    ├── void Initialize()
    ├── void Load(IPluginSensorsContainer container)
    └── void Close()
```

**Lifecycle:**
1. `Initialize()` - Start Python bridge, establish Named Pipe connection
2. `Load(container)` - Discover devices, create sensors, register with FanControl
3. `Update()` - Called every ~1 second to refresh sensor values
4. `Close()` - Shutdown bridge, cleanup resources

**RefreshRequested Event:**
- Signals FanControl that plugin needs complete refresh
- Use when recovering from critical errors or device reconnection
- FanControl will call `Load()` again

### IPluginControlSensor2 Interface

Control sensors implement `IPluginControlSensor2` for automatic sensor pairing:

```csharp
IPluginControlSensor2 : IPluginControlSensor
    └── string? PairedFanSensorId { get; }
```

**Auto-Linking Behavior:**
- During `Load()`, control sensors (duty %) are linked to speed sensors (RPM)
- Python bridge strips "duty" from channel names: "Pump duty" → "pump"
- Speed sensors keep their names: "Pump speed" → "pump speed"
- Auto-linking appends " speed" to control key to find matching sensor
- Example: Control "pump" + " speed" = "pump speed" → matches speed sensor

**Sensor ID Format:**
- `{device_description}/{channel_key}` with spaces removed
- Example: `"NZXTKrakenX63/pumpspeed"`

## Core Classes

### LiquidctlPlugin

**File:** `src/FanControl.Liquidctl/LiquidctlPlugin.cs`

**Responsibilities:**
- Main plugin entry point
- Implements IPlugin3 and IDisposable
- Manages sensor dictionary for quick lookups
- Filters and registers supported sensor types (°C, rpm, %)

**Important Implementation Details:**
```csharp
// Only these units are supported
List<string> supported_units = ["°C", "rpm", "%"];

// Control sensors (%) allow user adjustment
if (channel.Unit == "%") {
    ControlSensor sensor = new(device, channel, liquidctl);
    _container.ControlSensors.Add(sensor);
}

// Read-only sensors
if (channel.Unit == "rpm") { _container.FanSensors.Add(sensor); }
if (channel.Unit == "°C") { _container.TempSensors.Add(sensor); }
```

### LiquidctlBridgeWrapper

**File:** `src/FanControl.Liquidctl/LiquidctlBridgeWrapper.cs`

**Responsibilities:**
- Manages Python bridge process lifecycle
- Named Pipe client for IPC
- MessagePack serialization/deserialization
- Timeout handling (default: 5 seconds)

**Key Methods:**
- `Init()` - Start bridge process, establish connection
- `GetStatuses()` - Poll all device states (called in Update())
- `SetSpeed(device, channel, duty)` - Send control command to device
- `Shutdown()` - Gracefully terminate bridge process

### DeviceSensor & ControlSensor

**File:** `src/FanControl.Liquidctl/LiquidctlDevice.cs`

**DeviceSensor:**
- Base class for read-only sensors (temperature, RPM)
- Implements IPluginSensor
- Updates value from status polling

**ControlSensor:**
- Extends DeviceSensor
- Implements IPluginControlSensor2
- Allows user to set values (pump/fan duty)
- Communicates changes to bridge

## Communication Protocol

### Named Pipes

**Pipe Name:** `liquidctl_<random>`

**Message Format:** MessagePack

### Commands

> Note: The examples below show the logical structure of messages. Actual wire format is MessagePack binary.

#### Initialize
```json
{
  "command": "initialize"
}
```

#### Get Status
```json
{
  "command": "get_status"
}
```

**Response:**
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
        "key": "pump",
        "value": 75,
        "unit": "%"
      }
    ]
  }
]
```

#### Set Speed
```json
{
  "command": "set_speed",
  "device": "NZXT Kraken X63",
  "channel": "pump",
  "duty": 80
}
```

### Error Responses
```json
{
  "error": "Device not found",
  "code": "DEVICE_NOT_FOUND"
}
```

## Python Bridge

**Location:** `src/Liquidctl/`

**Key Files:**
- `liquidctl/__main__.py` - Entry point
- `liquidctl/bridge.py` - Main bridge logic
- `pyproject.toml` - Poetry configuration

**Important:**
- Uses PyInstaller to create standalone executable
- Must handle device initialization (liquidctl requires init before status)
- Strips "duty" from control channel names via `_formatString()`
- Manages device state and caching

## Data Models

### DeviceStatus
```csharp
public class DeviceStatus
{
    public string Description { get; set; }  // "NZXT Kraken X63"
    public List<StatusValue> Status { get; set; }
}
```

### StatusValue
```csharp
public class StatusValue
{
    public string Key { get; set; }      // "pump", "pump speed", etc.
    public float? Value { get; set; }    // Numeric value or null
    public string Unit { get; set; }     // "°C", "rpm", "%"
}
```

## Development Guidelines

### Error Handling

1. **Always catch exceptions in Update()** - Prevents FanControl crashes
2. **Use RefreshRequested for critical errors** - Allows recovery
3. **Log errors with IPluginLogger** - Helps debugging
4. **Graceful degradation** - Continue with available devices

**Example:**
```csharp
public void Update()
{
    try {
        var devices = liquidctl.GetStatuses();
        // Update sensors
    }
    catch (IOException ex) {
        logger.Log($"Bridge communication error: {ex.Message}");
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
```

### Performance Considerations

- `Update()` called every ~1 second - keep it lightweight
- Cache sensor objects - don't recreate each update
- Batch device status requests
- Use timeouts for bridge communication
- Avoid blocking operations

### Thread Safety

- FanControl may call methods from different threads
- Use thread-safe collections (ConcurrentDictionary) for sensor storage
- Protect bridge communication with locks if needed
- Ensure RefreshRequested event is thread-safe

### Testing

**Manual Testing:**
1. Copy DLL to FanControl plugins folder
2. Launch FanControl
3. Check logs for initialization
4. Verify sensors appear in UI

**Python Bridge Testing:**
```bash
cd src/Liquidctl
uv run pytest tests/
```

### Building

**C# Plugin:**
```bash
cd src/FanControl.Liquidctl
dotnet build
```

**Python Bridge:**
```bash
cd src/Liquidctl
uv sync
uv build
```

**Complete Release:**
```powershell
.\build.ps1
```

## Important Notes

### Device Support

- Plugin automatically detects all liquidctl-supported devices
- No hardcoded device names - works with any liquidctl device
- Only channels with supported units (°C, rpm, %) are registered

### Adding New Sensor Types

To support additional units (e.g., voltage):

1. Add unit to supported_units list in LiquidctlPlugin.cs
2. Create registration logic in Load() method
3. Add sensor to appropriate container

```csharp
List<string> supported_units = ["°C", "rpm", "%", "V"];

if (channel.Unit == "V") {
    VoltageSensor sensor = new(device, channel);
    _container.VoltageSensors.Add(sensor);
}
```

### Common Issues

**Bridge Not Starting:**
- Check Python executable is bundled correctly
- Verify Named Pipe permissions
- Check Windows Defender isn't blocking

**Device Not Detected:**
- Verify device is liquidctl-compatible
- Check USB connection and drivers
- Run liquidctl CLI directly to test

**Communication Timeout:**
- Default timeout is 5 seconds
- Increase if devices are slow to respond
- Check for USB issues or device errors

## References

- [FanControl Plugin SDK](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins)
- [liquidctl Documentation](https://github.com/liquidctl/liquidctl)
- [Named Pipes in .NET](https://docs.microsoft.com/en-us/dotnet/standard/io/pipe-operations)
- [PyInstaller](https://pyinstaller.org/)

## File Structure Reference

```
src/FanControl.Liquidctl/
├── LiquidctlPlugin.cs              # Main plugin class
├── LiquidctlBridgeWrapper.cs       # IPC client
├── LiquidctlDevice.cs              # Sensor classes
├── ref/
│   └── FanControl.Plugins.dll      # Plugin SDK
└── FanControl.Liquidctl.csproj

src/Liquidctl/
├── liquidctl/
│   ├── __main__.py                 # Entry point
│   └── bridge.py                   # Bridge logic
├── tests/
├── pyproject.toml                  # Project config
└── uv.lock                         # uv lockfile
```

## Version Information

- .NET Version: 8.0
- C# Language Version: Latest
- Python Version: 3.8+
- FanControl Plugin Interface: IPlugin3
- Target Platform: Windows (win-x64)
