import pytest
import msgspec

from liquidctl_server.models import (
    BadRequestException,
    BridgeResponse,
    DeviceStatus,
    FixedSpeedRequest,
    LedRequest,
    LiquidctlException,
    MessageStatus,
    PipeError,
    PipeRequest,
    SpeedKwargs,
    StatusValue,
)


class TestStatusValue:
    def test_fields(self):
        sv = StatusValue(key="Fan speed", value=1200.0, unit="rpm")
        assert sv.key == "Fan speed"
        assert sv.value == pytest.approx(1200.0)
        assert sv.unit == "rpm"

    def test_null_value(self):
        sv = StatusValue(key="Pump mode", value=None, unit="")
        assert sv.value is None


class TestMessageStatus:
    def test_success_value(self):
        assert MessageStatus.SUCCESS.value == "success"

    def test_error_value(self):
        assert MessageStatus.ERROR.value == "error"


class TestSpeedKwargs:
    def test_fields(self):
        sk = SpeedKwargs(channel="fan1", duty=75)
        assert sk.channel == "fan1"
        assert sk.duty == 75


class TestFixedSpeedRequest:
    def test_fields(self):
        req = FixedSpeedRequest(
            device_id=1, speed_kwargs=SpeedKwargs(channel="fan1", duty=50)
        )
        assert req.device_id == 1
        assert req.speed_kwargs.channel == "fan1"
        assert req.speed_kwargs.duty == 50


class TestLedRequest:
    def test_fields(self):
        req = LedRequest(
            device="Kraken X63",
            channel="ring",
            mode="fixed",
            colors=[[255, 0, 0]],
        )
        assert req.device == "Kraken X63"
        assert req.channel == "ring"
        assert req.mode == "fixed"
        assert req.colors == [[255, 0, 0]]


class TestPipeRequest:
    def test_command_only_defaults_data_to_null(self):
        req = PipeRequest(command="get.statuses")
        assert req.command == "get.statuses"
        assert bytes(req.data) == b"null"

    def test_with_explicit_data(self):
        req = PipeRequest(
            command="set.fixed_speed", data=msgspec.Raw(b'{"device_id": 1}')
        )
        assert b"device_id" in bytes(req.data)


class TestBridgeResponse:
    def test_success_response(self):
        resp = BridgeResponse(status=MessageStatus.SUCCESS, data=None)
        assert resp.status == MessageStatus.SUCCESS
        assert resp.error is None

    def test_error_response(self):
        resp = BridgeResponse(status=MessageStatus.ERROR, error="something failed")
        assert resp.status == MessageStatus.ERROR
        assert resp.error == "something failed"


class TestDeviceStatus:
    def test_fields_with_speed_channels(self):
        sv = StatusValue(key="Fan speed", value=800.0, unit="rpm")
        ds = DeviceStatus(
            id=1,
            description="Kraken X63",
            status=[sv],
            speed_channels=["fan1"],
        )
        assert ds.id == 1
        assert ds.description == "Kraken X63"
        assert ds.speed_channels == ["fan1"]

    def test_speed_channels_defaults_to_empty(self):
        ds = DeviceStatus(id=2, description="Something", status=[])
        assert ds.speed_channels == []


class TestExceptions:
    def test_liquidctl_exception(self):
        with pytest.raises(LiquidctlException, match="device error"):
            raise LiquidctlException("device error")

    def test_bad_request_exception(self):
        with pytest.raises(BadRequestException, match="not found"):
            raise BadRequestException("not found")

    def test_pipe_error(self):
        with pytest.raises(PipeError):
            raise PipeError("pipe broken")
