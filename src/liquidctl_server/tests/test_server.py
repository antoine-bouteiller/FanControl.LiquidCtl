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
    PipeError,
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


def _fake_pipe(messages):
    pipe = MagicMock()
    pipe.shutdown_event.is_set.side_effect = [False] * len(messages) + [True]
    pipe.read.side_effect = messages
    return pipe


class TestRunServerLoop:
    def test_message_is_processed_and_response_written(self):
        pipe = _fake_pipe([b'{"command":"get.statuses"}'])
        server.run_server_loop(_mock_service(), pipe)
        pipe.write.assert_called_once()

    def test_empty_read_sleeps_without_writing(self):
        pipe = _fake_pipe([None])
        with patch("liquidctl_server.server.time.sleep") as mock_sleep:
            server.run_server_loop(_mock_service(), pipe)
        mock_sleep.assert_called_once()
        pipe.write.assert_not_called()

    def test_pipe_error_on_write_is_swallowed(self):
        pipe = _fake_pipe([b'{"command":"get.statuses"}'])
        pipe.write.side_effect = PipeError("client gone")
        server.run_server_loop(_mock_service(), pipe)


def _make_context_manager(mock_cls):
    mock_cls.return_value.__enter__.return_value = mock_cls.return_value
    mock_cls.return_value.__exit__.return_value = False


@contextlib.contextmanager
def _patched_main():
    with (
        patch("liquidctl_server.server.LiquidctlService") as service_cls,
        patch("liquidctl_server.server.Server") as server_cls,
        patch("liquidctl_server.server.run_server_loop") as run_loop,
        patch("liquidctl_server.server.setup_logging") as setup_logging,
        patch("liquidctl_server.server.threading.Thread"),
    ):
        _make_context_manager(service_cls)
        _make_context_manager(server_cls)
        yield {
            "LiquidctlService": service_cls,
            "Server": server_cls,
            "run_server_loop": run_loop,
            "setup_logging": setup_logging,
        }


class TestMain:
    def test_initializes_service_and_runs_loop(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            server.main()
        mocks["LiquidctlService"].return_value.initialize_all.assert_called_once()
        mocks["run_server_loop"].assert_called_once()

    def test_default_pipe_names(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            server.main()
        names = [c.kwargs["name"] for c in mocks["Server"].call_args_list]
        assert names == ["LiquidCtlPipe", "LiquidCtlPipeRgb"]

    def test_test_flag_adds_suffix(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog", "--test"]):
            server.main()
        names = [c.kwargs["name"] for c in mocks["Server"].call_args_list]
        assert names == ["LiquidCtlPipeTest", "LiquidCtlPipeTestRgb"]

    def test_env_var_overrides_log_level(self):
        with (
            _patched_main() as mocks,
            patch.object(sys, "argv", ["prog", "--log-level", "INFO"]),
            patch.dict("os.environ", {"LIQUIDCTL_BRIDGE_LOG": "DEBUG"}),
        ):
            server.main()
        mocks["setup_logging"].assert_called_once_with("DEBUG")

    def test_keyboard_interrupt_is_handled(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            mocks["run_server_loop"].side_effect = KeyboardInterrupt
            server.main()

    def test_pipe_error_is_handled(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            mocks["run_server_loop"].side_effect = PipeError("closed")
            server.main()

    def test_fatal_exception_exits_with_code_1(self):
        with _patched_main() as mocks, patch.object(sys, "argv", ["prog"]):
            mocks["run_server_loop"].side_effect = RuntimeError("boom")
            with pytest.raises(SystemExit) as exc_info:
                server.main()
        assert exc_info.value.code == 1
