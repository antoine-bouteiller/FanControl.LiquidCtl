import contextlib
import json
import logging
import sys
from unittest.mock import MagicMock, patch

import msgspec
import pytest

from liquidctl_server import server
from liquidctl_server.models import (
    BadRequestException,
    BridgeResponse,
    MessageStatus,
)
from liquidctl_server.server import process_request


def _decode(raw: bytes) -> BridgeResponse:
    return msgspec.json.decode(raw, type=BridgeResponse)


def _mock_service(statuses=None):
    svc = MagicMock()
    svc.get_statuses.return_value = statuses or []
    svc.set_fixed_speed.return_value = None
    return svc


class TestGetStatuses:
    def test_success(self):
        svc = _mock_service()
        resp = _decode(process_request(b'{"command":"get.statuses"}', svc))
        assert resp.status == MessageStatus.SUCCESS
        svc.get_statuses.assert_called_once()


class TestSetFixedSpeed:
    def test_valid_payload_calls_service(self):
        svc = _mock_service()
        payload = json.dumps(
            {
                "command": "set.fixed_speed",
                "data": {
                    "device_id": 1,
                    "speed_kwargs": {"channel": "fan1", "duty": 50},
                },
            }
        ).encode()
        resp = _decode(process_request(payload, svc))
        assert resp.status == MessageStatus.SUCCESS
        svc.set_fixed_speed.assert_called_once_with(1, {"channel": "fan1", "duty": 50})

    def test_null_data_returns_error(self):
        svc = _mock_service()
        resp = _decode(process_request(b'{"command":"set.fixed_speed"}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "Missing data" in resp.error


class TestUnknownCommand:
    def test_unknown_command_returns_error(self):
        svc = _mock_service()
        resp = _decode(process_request(b'{"command":"does.not.exist"}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "Unknown command" in resp.error


class TestMalformedInput:
    def test_not_json_returns_protocol_error(self):
        svc = _mock_service()
        resp = _decode(process_request(b"not valid json", svc))
        assert resp.status == MessageStatus.ERROR
        assert "Protocol Error" in resp.error

    def test_wrong_schema_returns_protocol_error(self):
        svc = _mock_service()
        resp = _decode(process_request(b'{"wrong_field": 42}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "Protocol Error" in resp.error


class TestSetLed:
    def test_valid_payload_calls_set_color(self):
        svc = _mock_service()
        svc.set_color.return_value = None
        payload = json.dumps(
            {
                "command": "set.led",
                "data": {
                    "device": "Kraken X63",
                    "channel": "ring",
                    "mode": "fixed",
                    "colors": [[255, 0, 0], [0, 255, 0]],
                },
            }
        ).encode()
        resp = _decode(process_request(payload, svc))
        assert resp.status == MessageStatus.SUCCESS
        svc.set_color.assert_called_once_with(
            "Kraken X63", "ring", "fixed", [(255, 0, 0), (0, 255, 0)]
        )

    def test_null_data_returns_error(self):
        svc = _mock_service()
        resp = _decode(process_request(b'{"command":"set.led"}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "Missing data" in resp.error

    def test_short_color_returns_error(self):
        svc = _mock_service()
        payload = json.dumps(
            {
                "command": "set.led",
                "data": {
                    "device": "Kraken X63",
                    "channel": "ring",
                    "mode": "fixed",
                    "colors": [[255, 0]],
                },
            }
        ).encode()
        resp = _decode(process_request(payload, svc))
        assert resp.status == MessageStatus.ERROR
        assert "color" in resp.error
        svc.set_color.assert_not_called()

    def test_out_of_range_component_returns_error(self):
        svc = _mock_service()
        payload = json.dumps(
            {
                "command": "set.led",
                "data": {
                    "device": "Kraken X63",
                    "channel": "ring",
                    "mode": "fixed",
                    "colors": [[255, 0, 300]],
                },
            }
        ).encode()
        resp = _decode(process_request(payload, svc))
        assert resp.status == MessageStatus.ERROR
        assert "color" in resp.error
        svc.set_color.assert_not_called()


class TestHandlerExceptions:
    def test_bad_request_propagates_message(self):
        svc = _mock_service()
        svc.get_statuses.side_effect = BadRequestException("device 99 not found")
        resp = _decode(process_request(b'{"command":"get.statuses"}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "device 99 not found" in resp.error

    def test_generic_exception_returns_internal_error(self):
        svc = _mock_service()
        svc.get_statuses.side_effect = RuntimeError("USB exploded")
        resp = _decode(process_request(b'{"command":"get.statuses"}', svc))
        assert resp.status == MessageStatus.ERROR
        assert "Internal Error" in resp.error


class TestSetupLogging:
    def test_known_level_passed_to_basicconfig(self):
        with patch("liquidctl_server.server.logging.basicConfig") as mock_bc:
            server.setup_logging("DEBUG")
        assert mock_bc.call_args.kwargs["level"] == logging.DEBUG

    def test_unknown_level_falls_back_to_info(self):
        with patch("liquidctl_server.server.logging.basicConfig") as mock_bc:
            server.setup_logging("NOPE")
        assert mock_bc.call_args.kwargs["level"] == logging.INFO


def _make_context_manager(mock_cls):
    mock_cls.return_value.__enter__.return_value = mock_cls.return_value
    mock_cls.return_value.__exit__.return_value = False


@contextlib.contextmanager
def _patched_main():
    with (
        patch("liquidctl_server.server.LiquidctlService") as service_cls,
        patch("liquidctl_server.server.PipeServer") as pipe_server_cls,
        patch("liquidctl_server.server.setup_logging") as setup_logging,
        patch(
            "liquidctl_server.server.time.sleep", side_effect=KeyboardInterrupt
        ) as sleep,
    ):
        _make_context_manager(service_cls)
        yield {
            "LiquidctlService": service_cls,
            "PipeServer": pipe_server_cls,
            "setup_logging": setup_logging,
            "sleep": sleep,
        }


class TestMain:
    def test_initializes_service_and_starts_servers(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            server.main()
        mocks["LiquidctlService"].return_value.initialize_all.assert_called_once()
        assert mocks["PipeServer"].return_value.start.call_count == 2

    def test_default_pipe_names(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            server.main()
        names = [c.args[0] for c in mocks["PipeServer"].call_args_list]
        assert names == ["LiquidCtlPipe", "LiquidCtlPipeRgb"]

    def test_test_flag_adds_suffix(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog", "--test"]):
            server.main()
        names = [c.args[0] for c in mocks["PipeServer"].call_args_list]
        assert names == ["LiquidCtlPipeTest", "LiquidCtlPipeTestRgb"]

    def test_env_var_overrides_log_level(self):
        with (
            _patched_main() as mocks,
            patch.object(sys, "argv", ["prog", "--log-level", "INFO"]),
            patch.dict("os.environ", {"LIQUIDCTL_BRIDGE_LOG": "DEBUG"}),
        ):
            server.main()
        mocks["setup_logging"].assert_called_once_with("DEBUG")

    def test_keyboard_interrupt_stops_servers(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            server.main()
        assert mocks["PipeServer"].return_value.stop.call_count == 2

    def test_fatal_exception_exits_with_code_1(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            mocks[
                "LiquidctlService"
            ].return_value.initialize_all.side_effect = RuntimeError("boom")
            with pytest.raises(SystemExit) as exc_info:
                server.main()
        assert exc_info.value.code == 1

    def test_dead_pipe_server_exits_with_code_1_and_stops_servers(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            mocks["PipeServer"].return_value.is_alive.return_value = False
            with pytest.raises(SystemExit) as exc_info:
                server.main()
        assert exc_info.value.code == 1
        assert mocks["PipeServer"].return_value.stop.call_count == 2
