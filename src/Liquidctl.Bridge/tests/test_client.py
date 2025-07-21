import logging
from liquidctl_bridge.pipe_server import (
    Base,
    Mode,
    PipeError,
    ctypes_handle,
    kernel32,
    PIPE_READMODE_MESSAGE,
)
from liquidctl_bridge.models import FixedSpeedRequest, PipeRequest
import struct
import msgspec

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 0x00000003

logger = logging.getLogger(__name__)

encoder = msgspec.json.Encoder()


class TestClient(Base):
    def __init__(self, name: str, *, maxmessagesz: int = 4096) -> None:
        super().__init__(Mode.MASTER)
        self.maxmessagesz = maxmessagesz
        self.name = name

        pipe_name = f"\\\\.\\pipe\\{name}".encode("utf-8")
        self.handle = kernel32.CreateFileA(
            pipe_name,
            GENERIC_READ | GENERIC_WRITE,
            0,  # no sharing
            None,  # default security
            OPEN_EXISTING,
            0,  # default attributes
            None,  # no template file
        )

        if kernel32.GetLastError() != 0:
            open_error: int = kernel32.GetLastError()
            raise PipeError(f"Pipe Open Failed [{open_error}]")

        if not self.handle:
            raise RuntimeError("No connection")

        xmode = struct.pack("I", PIPE_READMODE_MESSAGE)
        ret: int = kernel32.SetNamedPipeHandleState(
            ctypes_handle(self.handle),
            xmode,
            None,
            None,
        )

        if ret == 0:
            state_error: int = kernel32.GetLastError()
            kernel32.CloseHandle(ctypes_handle(self.handle))
            raise PipeError(f"Pipe Set Mode Failed [{state_error}]")

        self.alive = True

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        if exc_type is KeyboardInterrupt:
            logger.info("KeyboardInterrupt detected, cleaning up TestClient...")
        elif exc_value is not None:
            logger.error(exc_value, traceback)

        try:
            self.close()
        except Exception as e:
            logger.error(f"Error during TestClient cleanup: {e}")

        return True

    def sendRequest(self, command: str, data: FixedSpeedRequest | None = None):
        self.write(encoder.encode(PipeRequest(command=command, data=data)))
        return self.read()

    def close(self) -> None:
        if self.handle is not None:
            kernel32.CloseHandle(ctypes_handle(self.handle))
        self.alive = False
