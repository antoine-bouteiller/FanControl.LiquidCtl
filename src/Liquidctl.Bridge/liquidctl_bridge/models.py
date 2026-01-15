from typing import List, Optional
import msgspec
from enum import Enum, IntEnum


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


class PipeRequest(msgspec.Struct):
    command: str
    data: Optional[FixedSpeedRequest] = None

class BridgeResponse(msgspec.Struct):
    status: MessageStatus
    data: Optional[Any] = None
    error: Optional[str] = None


class LiquidctlException(Exception):
    pass


class BadRequestException(Exception):
    pass


class DeviceStatus(msgspec.Struct):
    id: int
    description: str
    bus: str
    address: str
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
