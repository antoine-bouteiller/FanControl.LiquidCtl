from concurrent.futures import TimeoutError as FuturesTimeoutError
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


def _make_future(return_value=None):
    mock = MagicMock()
    mock.result.return_value = return_value
    return mock


class TestContextManager:
    def test_enter_returns_self(self):
        svc = _make_service()
        assert svc.__enter__() is svc

    def test_exit_calls_shutdown(self):
        svc = _make_service()
        svc._executor.submit.return_value = _make_future()
        svc.__exit__(None, None, None)
        svc._executor.shutdown.assert_called_once()

    def test_exit_does_not_suppress_exception(self):
        svc = _make_service()
        svc._executor.submit.return_value = _make_future()
        result = svc.__exit__(ValueError, ValueError("test"), None)
        assert not result


class TestResolveDeviceId:
    def test_found_case_insensitive(self):
        svc = _make_service()
        dev = MagicMock()
        dev.description = "NZXT Kraken X63"
        svc.devices = {1: dev}
        assert svc._resolve_device_id("kraken") == 1

    def test_not_found_returns_none(self):
        svc = _make_service()
        svc.devices = {}
        assert svc._resolve_device_id("unknown") is None


class TestSetColor:
    def test_success_calls_executor(self):
        svc = _make_service()
        dev = MagicMock()
        dev.description = "Kraken X63"
        svc.devices = {1: dev}
        svc._executor.submit.return_value = _make_future()

        svc.set_color("Kraken", "ring", "fixed", [(255, 0, 0)])

        svc._executor.submit.assert_called_once()

    def test_unknown_device_raises(self):
        svc = _make_service()
        svc.devices = {}
        with pytest.raises(BadRequestException, match="No device matching"):
            svc.set_color("NonExistent", "ring", "fixed", [(255, 0, 0)])

    def test_timeout_is_swallowed(self):
        svc = _make_service()
        dev = MagicMock()
        dev.description = "Kraken X63"
        svc.devices = {1: dev}
        future_mock = MagicMock()
        future_mock.result.side_effect = FuturesTimeoutError()
        svc._executor.submit.return_value = future_mock

        svc.set_color("Kraken", "ring", "fixed", [(255, 0, 0)])


class TestDisconnectAll:
    def test_calls_disconnect_per_device(self):
        svc = _make_service()
        svc.devices = {1: MagicMock(), 2: MagicMock()}
        svc._executor.submit.return_value = _make_future()

        svc.disconnect_all()

        assert svc._executor.submit.call_count == 2

    def test_exception_is_swallowed(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        future_mock = MagicMock()
        future_mock.result.side_effect = RuntimeError("USB error")
        svc._executor.submit.return_value = future_mock

        svc.disconnect_all()


class TestShutdown:
    def test_calls_executor_shutdown_and_clears_state(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.device_status_cache = {1: []}
        svc.speed_channels = {1: ["fan1"]}
        svc.previous_duty = {"1_fan1": 50}
        svc._executor.submit.return_value = _make_future()

        svc.shutdown()

        svc._executor.shutdown.assert_called_once()
        assert svc.devices == {}
        assert svc.device_status_cache == {}
        assert svc.speed_channels == {}
        assert svc.previous_duty == {}


class TestInitializeAll:
    def test_success_on_first_try(self):
        svc = _make_service()
        with patch.object(svc, "_find_devices") as mock_find:
            svc.initialize_all()
            mock_find.assert_called_once()

    def test_retries_on_failure_then_succeeds(self):
        svc = _make_service()
        call_count = 0

        def fail_then_succeed():
            nonlocal call_count
            call_count += 1
            if call_count < 2:
                raise RuntimeError("init failed")

        with patch.object(svc, "_find_devices", side_effect=fail_then_succeed):
            svc.initialize_all()
            assert call_count == 2
