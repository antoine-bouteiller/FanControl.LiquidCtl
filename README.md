# FanControl.Liquidctl

A FanControl plugin that integrates [liquidctl](https://github.com/liquidctl/liquidctl) to provide comprehensive sensor data and pump control for various All-in-One (AIO) liquid coolers and smart devices.

## Features

- **Temperature Monitoring**: Real-time fluid temperature readings from your AIO cooler
- **Pump Control**: Adjust pump speeds with precise duty cycle control
- **Fan Control**: Monitor and control fan speeds on supported devices
- **Wide Device Support**: Compatible with all [liquidctl supported devices](https://github.com/liquidctl/liquidctl#supported-devices)
- **Seamless Integration**: Works natively with FanControl's interface

## Tested Devices

- NZXT Kraken X63
- NZXT Smart Device V2

_Note: Should work with any liquidctl-supported device, but these are the ones we've specifically tested._

## Prerequisites

- Windows 11 22H2 or higher or .NET 8 installed manually
- [FanControl](https://github.com/Rem0o/FanControl.Releases) installed
- Compatible AIO cooler or smart device

## Installation

1. Download the latest release from the [Releases page](../../releases)
2. Extract the archive contents to your FanControl `Plugins` directory
   - Default location: `C:\Program files (x86)\FanControl\Plugins\`
3. Restart FanControl
4. Your liquidctl devices should appear automatically in the sensors and controls lists

## Architecture

The plugin consists of two main components:

### 1. .NET Plugin (`FanControl.Liquidctl`)

- Integrates with FanControl's plugin system
- Manages device discovery and sensor/control registration
- Handles communication with the Python bridge

### 2. Python Bridge (`liquidctl_bridge`)

- Standalone Python executable that wraps liquidctl functionality
- Communicates via Named Pipes for reliable IPC
- Handles all low-level device communication

## Usage

Once installed, the plugin automatically:

1. **Discovers** connected liquidctl-compatible devices
2. **Registers sensors** for temperature, pump speed, and fan speed monitoring
3. **Provides controls** for pump duty and fan speed adjustment
4. **Updates values** in real-time within FanControl

### Available Sensors

- **Fluid Temperature**: Coolant temperature readings
- **Pump Speed**: Current pump RPM
- **Fan Speed**: Fan RPM readings (where supported)

### Available Controls

- **Pump Duty**: Adjust pump speed (percentage-based)
- **Fan Control**: Control fan speeds (where supported)

## Troubleshooting

### Plugin Not Loading

- Ensure all files are extracted to the correct Plugins directory
- Check that your AIO is properly connected and recognized by Windows
- Restart FanControl after installation

### No Devices Detected

- Verify your device is [supported by liquidctl](https://github.com/liquidctl/liquidctl#supported-devices)
- Try running liquidctl directly from command line to test device connectivity
- Check Windows Device Manager for any driver issues

### Communication Errors

- The plugin automatically manages the Python bridge process
- If issues persist, try restarting FanControl
- Check FanControl logs for detailed error messages

## Development

### Prerequisites

- .NET 8 SDK
- Python 3.8+ with Poetry

### Python Bridge Development

```bash
cd src/Liquidctl.Bridge
poetry install
poetry run python liquidctl_bridge  # Run the bridge
poetry run python tests             # Run tests
```

### .NET Plugin Development

The project uses .NET 8 and includes all necessary references in the repository. Simply open the solution file and build.

### Building for Release

Use the provided `build.ps1` script to create a complete release package.

## Screenshots

![Fluid temperature sensor](/docs/images/FluidTemp.png)
![Pump speed and control](/docs/images/PumpControl.png)
