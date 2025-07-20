# FanControl.Liquidctl

This is a simple plugin that uses [liquidctl](https://github.com/liquidctl/liquidctl) to provide sensor data and pump control to variety of AIOs. So far it is tested with NZXT Kraken X63 and NZXT Smart Device V2 , but in principle shall work with [supported devices](https://github.com/liquidctl/liquidctl#supported-devices)

## Installation

Grab a release and unpack it to `Plugins` directory of your FanControl instalation. It contains a Windows Bundle of a python wrapper of liquidctl using [Named Pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes).

## Screenshots

![Fluid temperature sensor](/docs/images/FluidTemp.png)
![Pump speed and control](/docs/images/PumpControl.png)
