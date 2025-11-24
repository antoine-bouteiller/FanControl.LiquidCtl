"""Unit tests for models module."""

import pytest
import msgspec
from liquidctl_bridge.models import (
    StatusValue,
    MessageStatus,
    SpeedKwargs,
    FixedSpeedRequest,
    PipeRequest,
    DeviceStatus,
    Mode,
    PipeError,
    LiquidctlException,
    BadRequestException,
)


class TestStatusValue:
    """Test StatusValue struct."""

    def test_create_status_value(self):
        """Should create StatusValue with all fields."""
        status = StatusValue(key="temperature", value=28.5, unit="°C")
        assert status.key == "temperature"
        assert status.value == 28.5
        assert status.unit == "°C"

    def test_status_value_with_none(self):
        """Should allow None value."""
        status = StatusValue(key="firmware", value=None, unit="")
        assert status.key == "firmware"
        assert status.value is None
        assert status.unit == ""

    def test_status_value_serialization(self):
        """Should serialize to JSON correctly."""
        status = StatusValue(key="temperature", value=28.5, unit="°C")
        json_bytes = msgspec.json.encode(status)
        decoded = msgspec.json.decode(json_bytes, type=StatusValue)
        assert decoded.key == status.key
        assert decoded.value == status.value
        assert decoded.unit == status.unit


class TestMessageStatus:
    """Test MessageStatus enum."""

    def test_message_status_values(self):
        """Should have correct enum values."""
        assert MessageStatus.SUCCESS.value == "success"
        assert MessageStatus.ERROR.value == "error"


class TestSpeedKwargs:
    """Test SpeedKwargs struct."""

    def test_create_speed_kwargs(self):
        """Should create SpeedKwargs with channel and duty."""
        kwargs = SpeedKwargs(channel="pump", duty=75)
        assert kwargs.channel == "pump"
        assert kwargs.duty == 75

    def test_speed_kwargs_serialization(self):
        """Should serialize to JSON correctly."""
        kwargs = SpeedKwargs(channel="pump", duty=75)
        json_bytes = msgspec.json.encode(kwargs)
        decoded = msgspec.json.decode(json_bytes, type=SpeedKwargs)
        assert decoded.channel == kwargs.channel
        assert decoded.duty == kwargs.duty


class TestFixedSpeedRequest:
    """Test FixedSpeedRequest struct."""

    def test_create_fixed_speed_request(self):
        """Should create FixedSpeedRequest with device_id and speed_kwargs."""
        request = FixedSpeedRequest(
            device_id=1, speed_kwargs=SpeedKwargs(channel="pump", duty=75)
        )
        assert request.device_id == 1
        assert request.speed_kwargs.channel == "pump"
        assert request.speed_kwargs.duty == 75

    def test_fixed_speed_request_serialization(self):
        """Should serialize to JSON correctly."""
        request = FixedSpeedRequest(
            device_id=1, speed_kwargs=SpeedKwargs(channel="pump", duty=75)
        )
        json_bytes = msgspec.json.encode(request)
        decoded = msgspec.json.decode(json_bytes, type=FixedSpeedRequest)
        assert decoded.device_id == request.device_id
        assert decoded.speed_kwargs.channel == request.speed_kwargs.channel
        assert decoded.speed_kwargs.duty == request.speed_kwargs.duty


class TestPipeRequest:
    """Test PipeRequest struct."""

    def test_create_pipe_request_without_data(self):
        """Should create PipeRequest with just command."""
        request = PipeRequest(command="get.statuses")
        assert request.command == "get.statuses"
        assert request.data is None

    def test_create_pipe_request_with_data(self):
        """Should create PipeRequest with command and data."""
        request = PipeRequest(
            command="set.fixed_speed",
            data=FixedSpeedRequest(
                device_id=1, speed_kwargs=SpeedKwargs(channel="pump", duty=75)
            ),
        )
        assert request.command == "set.fixed_speed"
        assert request.data is not None
        assert request.data.device_id == 1

    def test_pipe_request_serialization(self):
        """Should serialize to JSON correctly."""
        request = PipeRequest(
            command="set.fixed_speed",
            data=FixedSpeedRequest(
                device_id=1, speed_kwargs=SpeedKwargs(channel="pump", duty=75)
            ),
        )
        json_bytes = msgspec.json.encode(request)
        decoded = msgspec.json.decode(json_bytes, type=PipeRequest)
        assert decoded.command == request.command
        assert decoded.data.device_id == request.data.device_id


class TestDeviceStatus:
    """Test DeviceStatus struct."""

    def test_create_device_status(self):
        """Should create DeviceStatus with all fields."""
        status = DeviceStatus(
            id=1,
            description="NZXT Kraken X53",
            bus="hid",
            address="1234:5678:00",
            status=[
                StatusValue(key="temperature", value=28.5, unit="°C"),
                StatusValue(key="pump speed", value=2500.0, unit="rpm"),
            ],
        )
        assert status.id == 1
        assert status.description == "NZXT Kraken X53"
        assert status.bus == "hid"
        assert status.address == "1234:5678:00"
        assert len(status.status) == 2

    def test_device_status_serialization(self):
        """Should serialize to JSON correctly."""
        device_status = DeviceStatus(
            id=1,
            description="NZXT Kraken X53",
            bus="hid",
            address="1234:5678:00",
            status=[StatusValue(key="temperature", value=28.5, unit="°C")],
        )
        json_bytes = msgspec.json.encode(device_status)
        decoded = msgspec.json.decode(json_bytes, type=DeviceStatus)
        assert decoded.id == device_status.id
        assert decoded.description == device_status.description
        assert len(decoded.status) == 1


class TestMode:
    """Test Mode enum."""

    def test_mode_values(self):
        """Should have correct enum values."""
        assert Mode.MASTER == 0
        assert Mode.SLAVE == 1

    def test_is_slave(self):
        """Should correctly identify slave mode."""
        assert Mode.is_slave(Mode.SLAVE) is True
        assert Mode.is_slave(Mode.MASTER) is False

    def test_is_master(self):
        """Should correctly identify master mode."""
        assert Mode.is_master(Mode.MASTER) is True
        assert Mode.is_master(Mode.SLAVE) is False


class TestExceptions:
    """Test custom exceptions."""

    def test_pipe_error(self):
        """Should raise PipeError."""
        with pytest.raises(PipeError, match="Pipe is dead"):
            raise PipeError("Pipe is dead")

    def test_liquidctl_exception(self):
        """Should raise LiquidctlException."""
        with pytest.raises(LiquidctlException, match="Device error"):
            raise LiquidctlException("Device error")

    def test_bad_request_exception(self):
        """Should raise BadRequestException."""
        with pytest.raises(BadRequestException, match="Invalid request"):
            raise BadRequestException("Invalid request")
