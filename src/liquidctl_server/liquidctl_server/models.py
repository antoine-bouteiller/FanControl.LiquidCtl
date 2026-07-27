from enum import Enum

import msgspec


class StatusValue(msgspec.Struct):
    key: str
    value: float | None
    unit: str


class MessageStatus(Enum):
    SUCCESS = "success"
    ERROR = "error"


class SpeedKwargs(msgspec.Struct):
    channel: str
    duty: int


class FixedSpeedRequest(msgspec.Struct):
    device_id: int
    speed_kwargs: SpeedKwargs


class LedRequest(msgspec.Struct):
    # device is matched against each liquidctl device's description (the RGB
    # plugin targets devices by name, not by integer id).
    device: str
    channel: str
    mode: str
    colors: list[list[int]]


class PipeRequest(msgspec.Struct):
    command: str
    # Decoded per-command (each command has its own payload shape). Kept as a
    # bare Raw, not Optional[Raw]: msgspec drops the Raw arm of Optional[Raw] and
    # would then reject any non-null data. Absent/null decodes to Raw(b"null").
    data: msgspec.Raw = msgspec.Raw(b"null")


class DeviceStatus(msgspec.Struct):
    id: int
    description: str
    status: list[StatusValue]
    speed_channels: list[str] = []


class BridgeResponse(msgspec.Struct):
    status: MessageStatus
    data: list[DeviceStatus] | str | None = None
    error: str | None = None


class LiquidctlException(Exception):
    pass


class BadRequestException(Exception):
    pass


class PipeError(Exception):
    """Custom exception for pipe operations."""
