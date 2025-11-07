# API Reference

## Plugin Interfaces

### IPlugin3

The main plugin interface that must be implemented.

```csharp
public interface IPlugin3 : IPlugin2
{
    event EventHandler? RefreshRequested;
}

public interface IPlugin2 : IPlugin
{
    void Update();
}

public interface IPlugin
{
    string Name { get; }
    void Initialize();
    void Load(IPluginSensorsContainer container);
    void Close();
}
```

### IPluginControlSensor2

Interface for control sensors with automatic pairing support.

```csharp
public interface IPluginControlSensor2 : IPluginControlSensor
{
    string? PairedFanSensorId { get; }
}

public interface IPluginControlSensor : IPluginSensor
{
    void Set(float val);
    void Reset();
}
```

#### Properties

##### PairedFanSensorId
```csharp
string? PairedFanSensorId { get; }
```
Associates a speed (RPM) sensor with this control sensor, enabling FanControl to automatically pair the two.

**Purpose:**
- Eliminates manual sensor pairing in FanControl UI
- Automatically links pump/fan duty controls with their RPM sensors
- Improves user experience with zero-configuration setup

**Returns:** ID of the paired fan sensor, or null if no pairing exists

#### Properties

##### Name
```csharp
string Name { get; }
```
Returns the display name of the plugin as shown in FanControl.

**Returns:** `"liquidctl"`

#### Methods

##### Initialize()
```csharp
void Initialize()
```
Called once when FanControl starts. Initializes the Python bridge process and establishes communication.

**Throws:**
- May throw exceptions if bridge initialization fails

##### Load(IPluginSensorsContainer container)
```csharp
void Load(IPluginSensorsContainer container)
```
Called after Initialize(). Discovers devices and registers sensors.

**Parameters:**
- `container`: Container for registering sensors and controls

**Behavior:**
- Discovers all connected liquidctl devices
- Creates sensor objects for supported channels
- Registers temperature sensors (°C)
- Registers fan sensors (rpm)
- Registers control sensors (%)

**Throws:**
- `ArgumentNullException`: If container is null

##### Update()
```csharp
void Update()
```
Called periodically (typically every second) to refresh sensor values.

**Behavior:**
- Polls device status from Python bridge
- Updates all registered sensor values
- Handles missing or invalid values gracefully

##### Close()
```csharp
void Close()
```
Called when FanControl shuts down. Performs cleanup.

**Behavior:**
- Shuts down Python bridge process
- Closes Named Pipe connections
- Releases resources

#### Events

##### RefreshRequested
```csharp
event EventHandler? RefreshRequested
```
Event that signals FanControl that the plugin needs a complete refresh.

**When to Invoke:**
- After recovering from a critical error
- When device list changes
- After communication failure with bridge

**Example:**
```csharp
try {
    // Device operation
} catch (CriticalException ex) {
    logger.Log($"Critical error: {ex.Message}");
    RefreshRequested?.Invoke(this, EventArgs.Empty);
}
```

## Sensor Classes

### DeviceSensor

Base class for read-only sensors (temperature and RPM).

```csharp
public class DeviceSensor : IPluginSensor
{
    public string Id { get; }
    public string Name { get; }
    public float? Value { get; protected set; }

    public void Update(StatusValue status);
}
```

#### Properties

##### Id
```csharp
string Id { get; }
```
Unique identifier for the sensor, formatted as:
`{device_description}:{channel_key}`

**Example:** `"NZXT Kraken X63:Liquid temperature"`

##### Name
```csharp
string Name { get; }
```
Human-readable name displayed in FanControl.

**Example:** `"NZXT Kraken X63 - Liquid temperature"`

##### Value
```csharp
float? Value { get; protected set; }
```
Current sensor value. Null if not available.

#### Methods

##### Update(StatusValue status)
```csharp
public void Update(StatusValue status)
```
Updates the sensor value from device status.

**Parameters:**
- `status`: Status value from device containing the new reading

### ControlSensor

Extends DeviceSensor to support user-adjustable controls with automatic speed sensor pairing.

```csharp
public class ControlSensor : DeviceSensor, IPluginControlSensor2
{
    public string? PairedFanSensorId { get; internal set; }
    public void Set(float val);
    public void Reset();
}
```

#### Properties

##### PairedFanSensorId
```csharp
string? PairedFanSensorId { get; internal set; }
```
ID of the automatically paired speed (RPM) sensor for this control.

**Returns:** Sensor ID of the paired fan sensor, or null if no pairing was found

**Auto-Linking Behavior:**
- Automatically set during the `Load()` method
- Links "Pump duty" controls to "Pump speed" sensors
- Links "Fan duty" controls to "Fan speed" sensors
- Uses intelligent pattern matching to find corresponding sensors

**Example:**
- Control: `"NZXTKrakenX63/Pumpduty"` (duty control)
- Paired:  `"NZXTKrakenX63/Pumpspeed"` (speed sensor)

#### Methods

##### Set(float val)
```csharp
public void Set(float val)
```
Sets the control value (pump/fan duty cycle).

**Parameters:**
- `val`: Duty cycle percentage (0-100)

**Behavior:**
- Validates value is in valid range
- Sends command to Python bridge
- Updates local value

**Example:**
```csharp
controlSensor.Set(75.0f); // Set to 75% duty
```

##### Reset()
```csharp
public void Reset()
```
Resets the control to its default value.

**Behavior:**
- Sets value to 0
- May restore device default settings

## Bridge Communication

### LiquidctlBridgeWrapper

Manages communication with the Python bridge process.

```csharp
public class LiquidctlBridgeWrapper : IDisposable
{
    public void Init();
    public IReadOnlyCollection<DeviceStatus> GetStatuses();
    public void SetSpeed(DeviceStatus device, StatusValue channel, int duty);
    public void Shutdown();
    public void Dispose();
}
```

#### Methods

##### Init()
```csharp
public void Init()
```
Initializes the bridge connection.

**Behavior:**
- Starts Python bridge process
- Establishes Named Pipe connection
- Sends initialize command
- Waits for ready confirmation

**Throws:**
- `InvalidOperationException`: If bridge fails to start
- `TimeoutException`: If connection times out

##### GetStatuses()
```csharp
public IReadOnlyCollection<DeviceStatus> GetStatuses()
```
Retrieves current status of all devices.

**Returns:**
- Collection of device statuses containing all sensor values

**Throws:**
- `IOException`: If communication fails
- `JsonException`: If response parsing fails

##### SetSpeed(DeviceStatus device, StatusValue channel, int duty)
```csharp
public void SetSpeed(DeviceStatus device, StatusValue channel, int duty)
```
Sets the speed/duty cycle for a device channel.

**Parameters:**
- `device`: Target device
- `channel`: Channel to control
- `duty`: Duty cycle percentage (0-100)

**Throws:**
- `ArgumentException`: If device or channel invalid
- `IOException`: If communication fails

##### Shutdown()
```csharp
public void Shutdown()
```
Gracefully shuts down the bridge process.

**Behavior:**
- Sends shutdown command
- Waits for process termination
- Closes Named Pipe
- Cleans up resources

##### Dispose()
```csharp
public void Dispose()
```
Implements IDisposable. Calls Shutdown() and performs cleanup.

## Data Models

### DeviceStatus

Represents the complete status of a device.

```csharp
public class DeviceStatus
{
    public string Description { get; set; }
    public List<StatusValue> Status { get; set; }
}
```

#### Properties

##### Description
```csharp
string Description { get; set; }
```
Human-readable device description.

**Example:** `"NZXT Kraken X63"`

##### Status
```csharp
List<StatusValue> Status { get; set; }
```
Collection of all channel values for the device.

### StatusValue

Represents a single sensor/control channel value.

```csharp
public class StatusValue
{
    public string Key { get; set; }
    public float? Value { get; set; }
    public string Unit { get; set; }
}
```

#### Properties

##### Key
```csharp
string Key { get; set; }
```
Channel identifier.

**Examples:**
- `"Liquid temperature"`
- `"Pump speed"`
- `"Pump duty"`

##### Value
```csharp
float? Value { get; set; }
```
Numeric value for the channel. Null if not available.

##### Unit
```csharp
string Unit { get; set; }
```
Unit of measurement.

**Supported Units:**
- `"°C"` - Temperature (Celsius)
- `"rpm"` - Rotations per minute
- `"％"` - Percentage (duty cycle)

## Extension Points

### Custom Device Support

To add support for new device types:

1. Ensure device is supported by liquidctl
2. No code changes needed - plugin automatically detects all liquidctl devices
3. Only channels with supported units (°C, rpm, %) are registered

### Custom Sensors

To add new sensor types:

1. Add unit to supported_units list in LiquidctlPlugin.cs
2. Create sensor registration logic in Load() method
3. Implement appropriate IPluginSensor interface

Example:
```csharp
List<string> supported_units = ["°C", "rpm", "%", "V"]; // Add voltage support

// In Load() method
if (channel.Unit == "V") {
    VoltageSensor sensor = new(device, channel);
    _container.VoltageSensors.Add(sensor);
}
```

## Error Handling

### Exception Strategy

The plugin uses a defensive error handling strategy:

1. **Catch exceptions in Update()** - Prevents FanControl crashes
2. **Log errors** - Uses IPluginLogger for diagnostics
3. **Graceful degradation** - Continues with available devices
4. **Use RefreshRequested** - Signals need for reinitialization

### Example Error Handling

```csharp
public void Update()
{
    try {
        IReadOnlyCollection<DeviceStatus> devices = liquidctl.GetStatuses();
        foreach (DeviceStatus device in devices) {
            UpdateDeviceSensors(device);
        }
    }
    catch (IOException ex) {
        logger.Log($"Bridge communication error: {ex.Message}");
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
    catch (Exception ex) {
        logger.Log($"Unexpected error in Update: {ex.Message}");
        // Continue without triggering refresh for non-critical errors
    }
}
```

## Performance Considerations

### Update Frequency

- Update() is called approximately once per second
- Keep operations lightweight
- Avoid blocking calls
- Use timeouts for bridge communication

### Memory Management

- Cache sensor objects - don't recreate each update
- Dispose of resources properly
- Use object pooling for frequent allocations if needed

### Bridge Communication

- Batch device status requests
- Use efficient JSON serialization
- Implement connection pooling if multiple commands needed
- Set appropriate timeouts (default: 5 seconds)

## Logging

Use the IPluginLogger for diagnostics:

```csharp
public class LiquidCtlPlugin(IPluginLogger logger) : IPlugin3
{
    private void LogInfo(string message)
    {
        logger.Log($"[liquidctl] {message}");
    }

    private void LogError(string message, Exception ex)
    {
        logger.Log($"[liquidctl] ERROR: {message} - {ex.Message}");
    }
}
```

**Best Practices:**
- Prefix messages with plugin name
- Include context for errors
- Log device discovery results
- Log bridge lifecycle events
- Avoid logging in Update() unless errors occur

## Thread Safety

### Considerations

- FanControl may call plugin methods from different threads
- Use thread-safe collections for sensor dictionary
- Protect bridge communication with locks if needed
- Ensure RefreshRequested event is thread-safe

### Example Thread-Safe Implementation

```csharp
private readonly object _lockObject = new();
private readonly ConcurrentDictionary<string, DeviceSensor> sensors = new();

public void Update()
{
    lock (_lockObject)
    {
        // Bridge communication
        IReadOnlyCollection<DeviceStatus> devices = liquidctl.GetStatuses();
        // Update sensors
    }
}
```
