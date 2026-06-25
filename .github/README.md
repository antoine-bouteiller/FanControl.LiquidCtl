# FanControl.Liquidctl

> **Important:** This plugin requires the **.NET 10** version of FanControl. Make sure you are running a FanControl release built on .NET 10.

A FanControl plugin that integrates [liquidctl](https://github.com/liquidctl/liquidctl) to provide comprehensive sensor data and pump control for various All-in-One (AIO) liquid coolers and smart devices.

## Features

- **Temperature Monitoring**: Real-time fluid temperature readings from your AIO cooler
- **Pump Control**: Adjust pump speeds with precise duty cycle control
- **Fan Control**: Monitor and control fan speeds on supported devices
- **RGB Lighting Endpoint**: Exposes a `set.led` command on a dedicated pipe so an external plugin can drive device lighting through the same serialized HID queue as the fans — no USB contention
- **Wide Device Support**: Compatible with all [liquidctl supported devices](https://github.com/liquidctl/liquidctl#supported-devices)
- **Seamless Integration**: Works natively with FanControl's interface
- **Error Recovery**: Automatic refresh and recovery from communication errors
- **Auto-Linking**: Automatically pairs control sensors with their corresponding speed sensors

## Tested Devices

- NZXT Kraken X63
- NZXT Smart Device V2

_Note: Should work with any liquidctl-supported device, but these are the ones I've specifically tested._

## Prerequisites

- Windows 11 22H2 or higher or .NET 10 installed manually
- [FanControl](https://github.com/Rem0o/FanControl.Releases) installed
- Compatible AIO cooler or smart device

## Installation

1. Download the latest release from the [Releases page](../../releases)
2. Extract the archive contents to your FanControl `Plugins` directory
   - Default location: `C:\Program files (x86)\FanControl\Plugins\`
3. Restart FanControl
4. Your liquidctl devices should appear automatically in the sensors and controls lists

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

## RGB Lighting (command sink)

Besides fans and pumps, the bridge exposes an RGB endpoint so another process can drive device lighting **through this plugin**, sharing the one serialized per-device HID queue — so RGB never contends with fan/pump commands on the same USB device (a separate RGB tool fighting for the same HID is the usual cause of dropped commands and stalled control).

- A **dedicated pipe** `LiquidCtlPipeRgb` (separate from the fan pipe, which a client holds open) accepts the `set.led` command.
- `set.led` applies per-LED colours to a channel of a device matched by description — e.g. the Kraken `ring`/`logo` or the Smart Device `led1`/`led2`.

```json
{
  "command": "set.led",
  "data": {
    "device": "Kraken",
    "channel": "ring",
    "mode": "super-fixed",
    "colors": [[255, 0, 0], [0, 255, 0], "... one [r,g,b] per LED"]
  }
}
```

This powers the NZXT command sink in [FanControl.Rgb](https://github.com/6wheels/FanControlPlugins): NZXT RGB reacts to FanControl sensors while liquidctl stays the sole owner of the HID.

## Screenshots

![Fluid temperature sensor](/docs/images/FluidTemp.png)
![Pump speed and control](/docs/images/PumpControl.png)

## Troubleshooting

### Plugin Not Loading

- Ensure all files are extracted to the correct Plugins directory
- Check that your AIO is properly connected and recognized by Windows
- Restart FanControl after installation
- Verify .NET 10 runtime is available

### No Devices Detected

- Verify your device is [supported by liquidctl](https://github.com/liquidctl/liquidctl#supported-devices)
- Try running liquidctl directly from command line to test device connectivity
- Check Windows Device Manager for any driver issues

### Communication Errors

- The plugin automatically manages the Python bridge process
- If issues persist, try restarting FanControl
- Check FanControl logs for detailed error messages

## Architecture

The plugin consists of two main components:

1. **.NET Plugin** - Integrates with FanControl's plugin system
2. **Python Bridge** - Wraps liquidctl functionality and communicates via Named Pipes

## Development

### Prerequisites

- .NET 10 SDK
- [uv](https://github.com/astral-sh/uv)

### Building

```bash
# Build C# plugin
cd src/FanControl.Liquidctl
dotnet build

# Build Python bridge
cd src/liquidctl_server
uv sync
uv build
```

### Repository Structure

```
FanControl.LiquidCtl/
├── src/
│   ├── FanControl.Liquidctl/    # C# Plugin
│   └── liquidctl_server/        # Python Server
└── docs/                        # Documentation & images
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes with clear commits
4. Add tests for new functionality
5. Submit pull request with description

## References

- [FanControl Plugin Documentation](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins)
- [liquidctl Documentation](https://github.com/liquidctl/liquidctl)
- [Coolercontrol](https://gitlab.com/coolercontrol/coolercontrol) most of the liquidctl wrapper code comes from it
