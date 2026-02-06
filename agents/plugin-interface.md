# Plugin Interface

## IPlugin3 Interface

The plugin implements `IPlugin3`, the latest FanControl plugin interface:

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

## Lifecycle

1. **`Initialize()`** - Start Python bridge, establish Named Pipe connection
2. **`Load(container)`** - Discover devices, create sensors, register with FanControl
3. **`Update()`** - Called every ~1 second to refresh sensor values
4. **`Close()`** - Shutdown bridge, cleanup resources

## RefreshRequested Event

- Signals FanControl that plugin needs complete refresh
- Use when recovering from critical errors or device reconnection
- FanControl will call `Load()` again

## IPluginControlSensor2 Interface

Control sensors implement `IPluginControlSensor2` for automatic sensor pairing:

```csharp
IPluginControlSensor2 : IPluginControlSensor
    └── string? PairedFanSensorId { get; }
```

## Auto-Linking Behavior

During `Load()`, control sensors (duty %) are linked to speed sensors (RPM):

1. Python bridge strips "duty" from channel names: `"Pump duty"` → `"pump"`
2. Speed sensors keep their names: `"Pump speed"` → `"pump speed"`
3. Auto-linking appends `" speed"` to control key to find matching sensor
4. Example: Control `"pump"` + `" speed"` = `"pump speed"` → matches speed sensor

## Sensor ID Format

- Format: `{device_description}/{channel_key}` with spaces removed
- Example: `"NZXTKrakenX63/pumpspeed"`
