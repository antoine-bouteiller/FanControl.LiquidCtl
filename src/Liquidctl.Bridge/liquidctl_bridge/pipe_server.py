import ctypes
import logging
import sys
import threading
import time
from typing import (
    Optional,
    Union,
)
from liquidctl_bridge.models import Mode, PipeError


PIPE_ACCESS_DUPLEX = 0x00000003
PIPE_TYPE_MESSAGE = 0x00000004
PIPE_READMODE_MESSAGE = 0x00000002
ERROR_PIPE_BUSY = 231
MAX_MESSAGE_SIZE = 4096
TIMEOUT = 100

try:
    kernel32 = ctypes.windll.kernel32
except AttributeError:
    raise RuntimeError("This module requires Windows OS")

logger = logging.getLogger(__name__)


def ctypes_handle(handle: int) -> Union[ctypes.c_uint, ctypes.c_ulonglong]:
    if sys.maxsize > 2**32:
        return ctypes.c_ulonglong(handle)
    else:
        return ctypes.c_uint(handle)


class Base:
    def __init__(self, mode: int = Mode.SLAVE):
        self.mode = mode
        self.alive = False
        self.handle: Optional[int] = None

    def read(self) -> Optional[bytes]:
        if not self.alive or not self.handle:
            return

        while not self.canread():
            time.sleep(0.2)

        buf = ctypes.create_string_buffer(4096)
        cnt_buffer = ctypes.c_uint(0)

        ret: bool = kernel32.ReadFile(
            ctypes_handle(self.handle),
            buf,
            4096,
            ctypes.byref(cnt_buffer),
            None,
        )

        if ret == 0:
            self.alive = False
            return

        cnt = cnt_buffer.value
        if cnt > 0:
            rawmsg: bytes = bytes(buf[:cnt])
            return rawmsg

    def canread(self) -> bool:
        if not self.alive or not self.handle:
            return False

        total_bytes_available = ctypes.c_uint(0)
        bytes_left_this_message = ctypes.c_uint(0)

        ret: int = kernel32.PeekNamedPipe(
            ctypes_handle(self.handle),
            None,
            0,
            None,
            ctypes.byref(total_bytes_available),
            ctypes.byref(bytes_left_this_message),
        )

        if ret == 0:
            self.alive = False
            return False

        return total_bytes_available.value > 0

    def write(self, message: bytes) -> bool:
        if not self.alive or not self.handle:
            raise PipeError("Pipe is dead!")

        written = ctypes.c_uint(0)

        ret: bool = kernel32.WriteFile(
            ctypes_handle(self.handle),
            message,
            len(message),
            ctypes.byref(written),
            None,
        )
        if ret == 0:
            self.alive = False

        return True


class Server(Base):
    def __init__(
        self,
        name: str,
    ) -> None:
        super().__init__(Mode.SLAVE)
        self.name = name
        self.shutdown = False
        self.hasdata = False

        self.server_thread = threading.Thread(target=self.serverentry, daemon=True)
        self.server_thread.start()

    def __enter__(self):
        logger.info("Starting Liquidctl Bridge Server")
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        if exc_type is KeyboardInterrupt:
            logger.info("KeyboardInterrupt detected, cleaning up Server...")
        elif exc_value is not None:
            logger.error(exc_value, traceback)

        try:
            self.close()
        except Exception as e:
            logger.error(f"Error during Server cleanup: {e}")

        return True

    def close_connection(self) -> None:
        if self.handle is not None:
            kernel32.CloseHandle(ctypes_handle(self.handle))
            self.handle = None
        self.alive = False

    def close(self) -> None:
        self.shutdown = True
        self.close_connection()

    def serverentry(self) -> None:
        logger.info(f"Starting Named Pipe server thread for pipe: {self.name}")
        while not self.shutdown:
            if self.handle is not None and (not self.alive and not self.canread()):
                self.close_connection()

            if self.handle is None:
                pipe_name = f"\\\\.\\pipe\\{self.name}".encode("utf-8")
                logger.debug(f"Creating Named Pipe: {pipe_name.decode('utf-8')}")

                nph = kernel32.CreateNamedPipeA(
                    pipe_name,
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE,
                    1,
                    MAX_MESSAGE_SIZE,
                    MAX_MESSAGE_SIZE,
                    TIMEOUT,
                    None,
                )

                pipe_error: int = kernel32.GetLastError()

                if pipe_error == ERROR_PIPE_BUSY:
                    logger.warning("Named Pipe is busy, retrying...")
                    time.sleep(2)
                    continue

                if nph == -1:
                    logger.error(f"Failed to create Named Pipe. Error code: {pipe_error}")
                    time.sleep(2)
                    continue

                logger.info("Named Pipe created, waiting for client connection...")
                ret: bool = kernel32.ConnectNamedPipe(
                    ctypes.c_uint(nph), ctypes.c_uint(0)
                )

                if not ret:
                    error_code = kernel32.GetLastError()
                    logger.warning(f"Failed to connect to client. Error code: {error_code}")
                    kernel32.CloseHandle(ctypes_handle(nph))
                    continue

                self.handle = nph
                self.alive = True
                logger.info("Client connected to Named Pipe")
            else:
                time.sleep(0.2)
