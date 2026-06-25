import json
from unittest.mock import MagicMock

import msgspec

from liquidctl_server.models import BadRequestException, BridgeResponse, MessageStatus
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
