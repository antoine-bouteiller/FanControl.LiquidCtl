# Architecture

## Component Structure

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

## Core Classes

### LiquidctlPlugin

**File:** `src/FanControl.Liquidctl/LiquidctlPlugin.cs`

- Main plugin entry point
- Implements IPlugin3 and IDisposable
- Manages sensor dictionary for quick lookups
- Filters and registers supported sensor types (°C, rpm, %)

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

## Python Bridge

**Location:** `src/Liquidctl/`

**Key Files:**

- `liquidctl/__main__.py` - Entry point
- `liquidctl/bridge.py` - Main bridge logic
- `pyproject.toml` - uv configuration

**Key behaviors:**

- Uses PyInstaller to create standalone executable
- Must handle device initialization (liquidctl requires init before status)
- Strips "duty" from control channel names via `_formatString()`
- Manages device state and caching

## Device Support

- Plugin automatically detects all liquidctl-supported devices
- No hardcoded device names - works with any liquidctl device
- Only channels with supported units (°C, rpm, %) are registered

### Adding New Sensor Types

To support additional units (e.g., voltage):

1. Add unit to `supported_units` list in `LiquidctlPlugin.cs`
2. Create registration logic in `Load()` method
3. Add sensor to appropriate container

```csharp
List<string> supported_units = ["°C", "rpm", "%", "V"];

if (channel.Unit == "V") {
    VoltageSensor sensor = new(device, channel);
    _container.VoltageSensors.Add(sensor);
}
```
