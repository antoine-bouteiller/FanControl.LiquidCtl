from enum import Enum, IntEnum
from typing import List, Optional, Union

import msgspec


class StatusValue(msgspec.Struct):
    key: str
    value: Optional[float]
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
    colors: List[List[int]]


class PipeRequest(msgspec.Struct):
    command: str
    # Decoded per-command (each command has its own payload shape).
    data: Optional[msgspec.Raw] = None


class BridgeResponse(msgspec.Struct):
    status: MessageStatus
    data: Optional[Union[List["DeviceStatus"], str]] = None
    error: Optional[str] = None


class LiquidctlException(Exception):
    pass


class BadRequestException(Exception):
    pass


class DeviceStatus(msgspec.Struct):
    id: int
    description: str
    status: List[StatusValue]


class Mode(IntEnum):
    """Pipe communication modes."""

    MASTER = 0
    SLAVE = 1

    @classmethod
    def is_slave(cls, mode: int) -> bool:
        return mode & 3 == cls.SLAVE

    @classmethod
    def is_master(cls, mode: int) -> bool:
        return mode & 3 == cls.MASTER


class PipeError(Exception):
    """Custom exception for pipe operations."""

    pass
