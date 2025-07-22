# FanControl.Liquidctl

This is a simple plugin that uses [liquidctl](https://github.com/liquidctl/liquidctl) to provide sensor data and pump control to variety of AIOs. So far it is tested with NZXT Kraken X63 and NZXT Smart Device V2 , but in principle shall work with [supported devices](https://github.com/liquidctl/liquidctl#supported-devices)

## Installation

Grab a release and unpack it to `Plugins` directory of your FanControl instalation. It contains a Windows Bundle of a liquidctl python wrapper using [Named Pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes).

## Dev

The python wrapper use poetry to manager dependencies. You can launch it with `poetry run python liquidctl_bridge` while in the python folder. If you want to test easily the wrapper without building and launching .NET you use the test file with `poetry run python test` it does basic call to the differents named pipe routes.

The FanControl plugins uses .NET 8. References are included in the repo so the project should be buildable without any additional setup.

## Screenshots

![Fluid temperature sensor](/docs/images/FluidTemp.png)
![Pump speed and control](/docs/images/PumpControl.png)
