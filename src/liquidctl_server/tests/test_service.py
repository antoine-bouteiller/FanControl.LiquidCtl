from unittest.mock import MagicMock, patch

import pytest

from liquidctl_server.models import BadRequestException, StatusValue
from liquidctl_server.service.liquidctl_service import LiquidctlService


def _make_service():
    with patch("liquidctl_server.service.liquidctl_service.DeviceExecutor"):
        return LiquidctlService()


class TestStringifyStatus:
    def test_none_returns_empty(self):
        assert LiquidctlService._stringify_status(None) == []

    def test_empty_list_returns_empty(self):
        assert LiquidctlService._stringify_status([]) == []

    def test_numeric_value_parsed(self):
        result = LiquidctlService._stringify_status([("Fan speed", 1200, "rpm")])
        assert len(result) == 1
        assert result[0].key == "Fan speed"
        assert result[0].value == pytest.approx(1200.0)
        assert result[0].unit == "rpm"

    def test_float_value_parsed(self):
        result = LiquidctlService._stringify_status(
            [("Liquid temperature", 27.5, "°C")]
        )
        assert result[0].value == pytest.approx(27.5)

    def test_string_value_becomes_none(self):
        result = LiquidctlService._stringify_status([("Pump mode", "balanced", "")])
        assert result[0].value is None
        assert result[0].key == "Pump mode"

    def test_mixed_list(self):
        raw = [("Temp", 30.0, "°C"), ("Mode", "quiet", ""), ("Fan speed", 800, "rpm")]
        result = LiquidctlService._stringify_status(raw)
        assert result[0].value == pytest.approx(30.0)
        assert result[1].value is None
        assert result[2].value == pytest.approx(800.0)


class TestBuildDeviceStatus:
    def test_correct_fields(self):
        svc = _make_service()
        svc.speed_channels = {1: ["fan1", "fan2"]}
        lc_device = MagicMock()
        lc_device.description = "Corsair H100i Platinum"
        status_values = [StatusValue(key="Fan 1 speed", value=1200.0, unit="rpm")]

        result = svc._build_device_status(1, lc_device, status_values)

        assert result.id == 1
        assert result.description == "Corsair H100i Platinum"
        assert result.status == status_values
        assert result.speed_channels == ["fan1", "fan2"]

    def test_missing_speed_channels_defaults_to_empty(self):
        svc = _make_service()
        svc.speed_channels = {}
        lc_device = MagicMock()
        lc_device.description = "NZXT Kraken X63"

        result = svc._build_device_status(1, lc_device, [])

        assert result.speed_channels == []


class TestBuildStatusFromCache:
    def test_no_cache_returns_none(self):
        svc = _make_service()
        svc.device_status_cache = {}
        assert svc._build_status_from_cache(1, MagicMock()) is None

    def test_cached_values_returned(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        cached = [StatusValue(key="Liquid temperature", value=28.0, unit="°C")]
        svc.device_status_cache = {1: cached}
        lc_device = MagicMock()
        lc_device.description = "NZXT Kraken X63"

        result = svc._build_status_from_cache(1, lc_device)

        assert result is not None
        assert result.status == cached


class TestSetFixedSpeedDeduplication:
    def test_unknown_device_raises_bad_request(self):
        svc = _make_service()
        svc.devices = {}
        with pytest.raises(BadRequestException, match="not found"):
            svc.set_fixed_speed(99, {"channel": "fan1", "duty": 50})

    def test_same_duty_skips_executor(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {"1_fan1": 50}

        svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        svc._executor.submit.assert_not_called()

    def test_changed_duty_submits_and_updates_cache(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {"1_fan1": 30}
        future_mock = MagicMock()
        future_mock.result.return_value = None
        svc._executor.submit.return_value = future_mock

        svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        svc._executor.submit.assert_called_once()
        assert svc.previous_duty["1_fan1"] == 50

    def test_no_prior_entry_submits(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {}
        future_mock = MagicMock()
        future_mock.result.return_value = None
        svc._executor.submit.return_value = future_mock

        svc.set_fixed_speed(1, {"channel": "pump", "duty": 100})

        svc._executor.submit.assert_called_once()
