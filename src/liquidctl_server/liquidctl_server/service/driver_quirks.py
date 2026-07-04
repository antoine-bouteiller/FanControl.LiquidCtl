from typing import List

from liquidctl.driver.base import BaseDriver
from liquidctl.driver.commander_pro import CommanderPro
from liquidctl.driver.hydro_platinum import HydroPlatinum
from liquidctl.driver.smart_device import SmartDevice, SmartDevice2

try:
    from liquidctl.driver.aquacomputer import Aquacomputer
except ImportError:
    Aquacomputer = None

try:
    from liquidctl.driver.commander_core import CommanderCore
except ImportError:
    CommanderCore = None

try:
    from liquidctl.driver.smart_device import H1V2
except ImportError:
    H1V2 = None

try:
    from liquidctl.driver.smart_device import ControlHub
except ImportError:
    ControlHub = None

try:
    from liquidctl.driver.hydro_pro import HydroPro
except ImportError:
    HydroPro = None


def _is_a(device: BaseDriver, driver_cls) -> bool:
    return driver_cls is not None and isinstance(device, driver_cls)


def speed_channels(device: BaseDriver) -> List[str]:
    """Controllable speed channels reported by the driver (no uniform API exists)."""
    if (
        isinstance(device, (SmartDevice2, SmartDevice))
        or _is_a(device, ControlHub)
        or _is_a(device, H1V2)
    ):
        return list(getattr(device, "_speed_channels", {}).keys())
    if _is_a(device, Aquacomputer):
        return list(getattr(device, "_device_info", {}).get("fan_ctrl", {}).keys())
    if _is_a(device, CommanderCore):
        return ["pump"] if getattr(device, "_has_pump", False) else []
    if isinstance(device, (CommanderPro, HydroPlatinum)):
        return list(getattr(device, "_fan_names", []))
    if _is_a(device, HydroPro):
        return [f"fan{i + 1}" for i in range(getattr(device, "_fan_count", 0))]
    return []
