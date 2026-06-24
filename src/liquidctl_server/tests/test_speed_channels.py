from liquidctl.driver.commander_core import CommanderCore
from liquidctl.driver.commander_pro import CommanderPro

from liquidctl_server.service.liquidctl_service import LiquidctlService


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


def main():
    test_commander_pro_uses_fan_names()
    test_commander_core_pump_gated_on_has_pump()
    test_unknown_device_has_no_speed_channels()
    print("test_speed_channels: OK")


if __name__ == "__main__":
    main()
