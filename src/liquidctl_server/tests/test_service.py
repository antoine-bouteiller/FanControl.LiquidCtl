import logging
import re
import time
from concurrent.futures import TimeoutError as FuturesTimeoutError
from unittest.mock import MagicMock, patch

import pytest

from liquidctl_server.models import (
    BadRequestException,
    LiquidctlException,
    StatusValue,
)
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

    def test_same_fresh_duty_skips_executor(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {"1_fan1": (50, time.monotonic())}

        svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        svc._executor.submit.assert_not_called()

    def test_same_duty_past_ttl_submits_again(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {"1_fan1": (50, time.monotonic() - 3600)}
        future_mock = MagicMock()
        future_mock.result.return_value = None
        svc._executor.submit.return_value = future_mock

        svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        svc._executor.submit.assert_called_once()

    def test_changed_duty_submits_and_updates_cache(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {"1_fan1": (30, time.monotonic())}
        future_mock = MagicMock()
        future_mock.result.return_value = None
        svc._executor.submit.return_value = future_mock

        svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        svc._executor.submit.assert_called_once()
        assert svc.previous_duty["1_fan1"][0] == 50

    def test_no_prior_entry_submits(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {}
        future_mock = MagicMock()
        future_mock.result.return_value = None
        svc._executor.submit.return_value = future_mock

        svc.set_fixed_speed(1, {"channel": "pump", "duty": 100})

        svc._executor.submit.assert_called_once()

    def test_timeout_raises_and_keeps_cache_unset(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {}
        future_mock = MagicMock()
        future_mock.result.side_effect = FuturesTimeoutError()
        svc._executor.submit.return_value = future_mock

        with pytest.raises(LiquidctlException, match="Timeout setting speed"):
            svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})

        assert svc.previous_duty == {}

    def test_device_error_propagates(self):
        svc = _make_service()
        svc.devices = {1: MagicMock()}
        svc.previous_duty = {}
        future_mock = MagicMock()
        future_mock.result.side_effect = RuntimeError("could not write to device")
        svc._executor.submit.return_value = future_mock

        with pytest.raises(RuntimeError, match="could not write to device"):
            svc.set_fixed_speed(1, {"channel": "fan1", "duty": 50})


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

    def test_timeout_raises(self):
        svc = _make_service()
        dev = MagicMock()
        dev.description = "Kraken X63"
        svc.devices = {1: dev}
        future_mock = MagicMock()
        future_mock.result.side_effect = FuturesTimeoutError()
        svc._executor.submit.return_value = future_mock

        with pytest.raises(LiquidctlException, match="Timeout setting color"):
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

    def test_all_retries_fail_logs_error(self, caplog):
        svc = _make_service()
        with (
            patch.object(svc, "_find_devices", side_effect=RuntimeError("nope")),
            caplog.at_level(logging.ERROR),
        ):
            svc.initialize_all()
        assert "Failed to initialize devices" in caplog.text


class TestFindDevicesDiscovery:
    def test_value_error_returns_early(self):
        svc = _make_service()
        with patch(
            "liquidctl_server.service.liquidctl_service.liquidctl"
            ".find_liquidctl_devices",
            side_effect=ValueError("no backend"),
        ):
            svc._find_devices()
        assert svc.devices == {}

    def test_no_devices_returns_early(self):
        svc = _make_service()
        with patch(
            "liquidctl_server.service.liquidctl_service.liquidctl"
            ".find_liquidctl_devices",
            return_value=[],
        ):
            svc._find_devices()
        assert svc.devices == {}

    def test_connect_failure_is_logged_and_skipped(self, caplog):
        svc = _make_service()
        good = _device("NZXT Kraken X63")
        bad = _device("Broken Device")

        def connect(device_id, lc_device):
            if lc_device is bad:
                raise RuntimeError("USB error")

        with (
            patch(
                "liquidctl_server.service.liquidctl_service.liquidctl"
                ".find_liquidctl_devices",
                return_value=[good, bad],
            ),
            patch(
                "liquidctl_server.service.liquidctl_service.load_device_filter",
                return_value=None,
            ),
            patch.object(svc, "_connect_device", side_effect=connect),
            caplog.at_level(logging.ERROR),
        ):
            svc._find_devices()

        assert list(svc.devices.values()) == [good]
        assert "Failed to connect device" in caplog.text


class TestConnectDevice:
    def test_success_records_speed_channels(self):
        svc = _make_service()
        svc._executor.submit.return_value = _make_future()
        dev = _device("NZXT Kraken X63")

        with patch(
            "liquidctl_server.service.liquidctl_service.driver_quirks.speed_channels",
            return_value=["fan1", "pump"],
        ):
            svc._connect_device(1, dev)

        assert svc.speed_channels[1] == ["fan1", "pump"]

    def test_already_open_is_warning_not_error(self, caplog):
        svc = _make_service()
        future = MagicMock()
        future.result.side_effect = RuntimeError("device already open")
        svc._executor.submit.return_value = future

        with caplog.at_level(logging.WARNING):
            svc._connect_device(1, _device("Kraken"))

        assert "already connected" in caplog.text

    def test_other_runtime_error_wrapped(self):
        svc = _make_service()
        future = MagicMock()
        future.result.side_effect = RuntimeError("bus fault")
        svc._executor.submit.return_value = future

        with pytest.raises(LiquidctlException, match="Device connection error"):
            svc._connect_device(1, _device("Kraken"))


class TestGetStatuses:
    def test_no_devices_returns_empty(self):
        svc = _make_service()
        svc.devices = {}
        assert svc.get_statuses() == []

    def test_collects_present_statuses(self):
        svc = _make_service()
        svc.devices = {1: _device("A"), 2: _device("B")}
        sentinel = object()
        with patch.object(
            svc,
            "_get_current_or_cached_device_status",
            side_effect=[sentinel, None],
        ):
            result = svc.get_statuses()
        assert result == [sentinel]


class TestGetCurrentOrCachedStatus:
    def test_success_caches_and_builds(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        dev = _device("Kraken")
        future = _make_future([("Fan", 1200, "rpm")])
        svc._executor.submit.return_value = future

        result = svc._get_current_or_cached_device_status(1, dev)

        assert result.description == "Kraken"
        assert svc.device_status_cache[1][0].value == pytest.approx(1200.0)
        future.cancel.assert_called_once()

    def test_timeout_delegates_to_handler(self):
        svc = _make_service()
        future = MagicMock()
        future.result.side_effect = FuturesTimeoutError()
        svc._executor.submit.return_value = future

        sentinel = object()
        with patch.object(
            svc, "_handle_status_timeout", return_value=sentinel
        ) as handler:
            result = svc._get_current_or_cached_device_status(1, _device("Kraken"))

        assert result is sentinel
        handler.assert_called_once()

    def test_generic_error_falls_back_to_cache(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        svc.device_status_cache = {1: [StatusValue(key="T", value=28.0, unit="°C")]}
        future = MagicMock()
        future.result.side_effect = RuntimeError("read fail")
        svc._executor.submit.return_value = future

        result = svc._get_current_or_cached_device_status(1, _device("Kraken"))

        assert result.status[0].value == pytest.approx(28.0)


class TestHandleStatusTimeout:
    def test_returns_cache_when_queue_busy(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        svc.device_status_cache = {1: [StatusValue(key="T", value=28.0, unit="°C")]}
        svc._executor.device_queue_empty.return_value = False

        result = svc._handle_status_timeout(1, _device("Kraken"))

        assert result.status[0].value == pytest.approx(28.0)
        svc._executor.submit.assert_not_called()

    def test_queue_empty_with_cache_submits_async_and_returns_cache(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        svc.device_status_cache = {1: [StatusValue(key="T", value=28.0, unit="°C")]}
        svc._executor.device_queue_empty.return_value = True
        svc._executor.submit.return_value = _make_future()

        result = svc._handle_status_timeout(1, _device("Kraken"))

        assert result.status[0].value == pytest.approx(28.0)
        svc._executor.submit.assert_called_once()

    def test_queue_empty_no_cache_awaits_async(self):
        svc = _make_service()
        svc.device_status_cache = {}
        svc._executor.device_queue_empty.return_value = True
        sentinel = object()
        async_future = _make_future(sentinel)
        svc._executor.submit.return_value = async_future

        result = svc._handle_status_timeout(1, _device("Kraken"))

        assert result is sentinel
        async_future.cancel.assert_called_once()

    def test_queue_empty_no_cache_async_times_out(self):
        svc = _make_service()
        svc.device_status_cache = {}
        svc._executor.device_queue_empty.return_value = True
        async_future = MagicMock()
        async_future.result.side_effect = FuturesTimeoutError()
        svc._executor.submit.return_value = async_future

        result = svc._handle_status_timeout(1, _device("Kraken"))

        assert result is None


class TestLongAsyncStatusRequest:
    def test_updates_cache_and_builds(self):
        svc = _make_service()
        svc.speed_channels = {1: []}
        dev = _device("Kraken")
        dev.get_status.return_value = [("Fan", 900, "rpm")]
        svc.devices = {1: dev}

        result = svc._long_async_status_request(1)

        assert result.status[0].value == pytest.approx(900.0)
        assert svc.device_status_cache[1][0].value == pytest.approx(900.0)


class TestLogDeviceDetails:
    def test_logs_inventory_without_error(self, caplog):
        svc = _make_service()
        dev = _device("Kraken")
        dev.vendor_id = 0x1E71
        dev.product_id = 0x2007
        dev._fan_count = 3
        svc.devices = {1: dev}

        with caplog.at_level(logging.INFO):
            svc.log_device_details()

        assert "Device inventory" in caplog.text
        assert "_fan_count" in caplog.text


def _device(description):
    dev = MagicMock()
    dev.description = description
    return dev


class TestFindDevicesFilter:
    def _run(self, svc, devices, filter_obj):
        with (
            patch(
                "liquidctl_server.service.liquidctl_service.liquidctl"
                ".find_liquidctl_devices",
                return_value=devices,
            ),
            patch(
                "liquidctl_server.service.liquidctl_service.load_device_filter",
                return_value=filter_obj,
            ),
            patch.object(svc, "_connect_device"),
        ):
            svc._find_devices()

    def test_no_filter_connects_all(self):
        svc = _make_service()
        devices = [_device("NZXT Kraken X63"), _device("ASUS Aura LED Controller")]

        self._run(svc, devices, None)

        assert len(svc.devices) == 2
        svc._executor.set_number_of_devices.assert_called_once_with(2)

    def test_filter_keeps_only_matching(self, caplog):
        svc = _make_service()
        aura = _device("ASUS Aura LED Controller")
        devices = [_device("NZXT Kraken X63"), aura]

        with caplog.at_level(
            logging.INFO, logger="liquidctl_server.service.liquidctl_service"
        ):
            self._run(svc, devices, re.compile("NZXT", re.IGNORECASE))

        assert [d.description for d in svc.devices.values()] == ["NZXT Kraken X63"]
        svc._executor.set_number_of_devices.assert_called_once_with(1)
        assert "Devices skipped by filter" in caplog.text

    def test_filter_matching_all_logs_no_skip(self, caplog):
        svc = _make_service()
        devices = [_device("NZXT Kraken X63"), _device("NZXT Smart Device V2")]

        with caplog.at_level(
            logging.INFO, logger="liquidctl_server.service.liquidctl_service"
        ):
            self._run(svc, devices, re.compile("NZXT", re.IGNORECASE))

        assert len(svc.devices) == 2
        assert "Devices skipped by filter" not in caplog.text

    def test_filter_removing_all_returns_early(self):
        svc = _make_service()
        devices = [_device("ASUS Aura LED Controller")]

        self._run(svc, devices, re.compile("NZXT", re.IGNORECASE))

        assert svc.devices == {}
        svc._executor.set_number_of_devices.assert_not_called()
