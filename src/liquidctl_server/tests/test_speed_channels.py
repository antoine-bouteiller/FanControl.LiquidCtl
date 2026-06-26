import pytest
from liquidctl.driver.commander_core import CommanderCore
from liquidctl.driver.commander_pro import CommanderPro
from liquidctl.driver.hydro_platinum import HydroPlatinum
from liquidctl.driver.smart_device import SmartDevice, SmartDevice2

from liquidctl_server.service.liquidctl_service import (
    Aquacomputer,
    ControlHub,
    H1V2,
    HydroPro,
    LiquidctlService,
)


def _fake(driver_cls, **attrs):
    instance = driver_cls.__new__(driver_cls)
    for name, value in attrs.items():
        setattr(instance, name, value)
    return instance


def test_commander_pro_uses_fan_names():
    device = _fake(CommanderPro, _fan_names=["fan1", "fan2", "fan3"])
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2", "fan3"]


def test_commander_core_pump_gated_on_has_pump():
    assert LiquidctlService._get_speed_channels(
        _fake(CommanderCore, _has_pump=True)
    ) == ["pump"]
    assert (
        LiquidctlService._get_speed_channels(_fake(CommanderCore, _has_pump=False))
        == []
    )


def test_unknown_device_has_no_speed_channels():
    assert LiquidctlService._get_speed_channels(object()) == []


def test_smart_device2_uses_speed_channels_keys():
    device = _fake(
        SmartDevice2, _speed_channels={"fan1": (0, False), "fan2": (1, False)}
    )
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2"]


def test_hydro_platinum_uses_fan_names():
    device = _fake(HydroPlatinum, _fan_names=["fan1", "fan2"])
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2"]


def test_hydro_platinum_empty_fan_names():
    device = _fake(HydroPlatinum, _fan_names=[])
    assert LiquidctlService._get_speed_channels(device) == []


@pytest.mark.skipif(HydroPro is None, reason="HydroPro not in this liquidctl version")
def test_hydro_pro_uses_fan_count():
    device = _fake(HydroPro, _fan_count=3)
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2", "fan3"]


def test_smart_device_uses_speed_channels_keys():
    device = _fake(SmartDevice, _speed_channels={"fan1": (0, False)})
    assert LiquidctlService._get_speed_channels(device) == ["fan1"]


def test_smart_device2_missing_attr_returns_empty():
    assert LiquidctlService._get_speed_channels(_fake(SmartDevice2)) == []


@pytest.mark.skipif(
    Aquacomputer is None, reason="Aquacomputer not in this liquidctl version"
)
def test_aquacomputer_uses_fan_ctrl_keys():
    device = _fake(
        Aquacomputer, _device_info={"fan_ctrl": {"fan1": None, "fan2": None}}
    )
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2"]


@pytest.mark.skipif(
    ControlHub is None, reason="ControlHub not in this liquidctl version"
)
def test_control_hub_uses_speed_channels_keys():
    device = _fake(ControlHub, _speed_channels={"fan1": (0, False), "fan2": (1, False)})
    assert LiquidctlService._get_speed_channels(device) == ["fan1", "fan2"]


@pytest.mark.skipif(H1V2 is None, reason="H1V2 not in this liquidctl version")
def test_h1v2_uses_speed_channels_keys():
    device = _fake(H1V2, _speed_channels={"fan1": (0, False)})
    assert LiquidctlService._get_speed_channels(device) == ["fan1"]


def main():
    test_commander_pro_uses_fan_names()
    test_commander_core_pump_gated_on_has_pump()
    test_unknown_device_has_no_speed_channels()
    test_smart_device2_uses_speed_channels_keys()
    test_hydro_platinum_uses_fan_names()
    test_hydro_platinum_empty_fan_names()
    print("test_speed_channels: OK")


if __name__ == "__main__":
    main()
