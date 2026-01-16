import ctypes
import logging
import time
from ctypes import wintypes

import msgspec

from liquidctl_bridge.models import BridgeResponse, FixedSpeedRequest, PipeRequest
from liquidctl_bridge.pipe_server import (
    INVALID_HANDLE_VALUE,
    KERNEL32,
    PIPE_READMODE_MESSAGE,
    Base,
    Mode,
    PipeError,
)

# --- Client-Specific Win32 Definitions ---
# These are not in the server base, so we define them here using the same KERNEL32 instance
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 0x00000003

# Define CreateFileW (Unicode)
KERNEL32.CreateFileW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
    wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD,
    wintypes.HANDLE
]
KERNEL32.CreateFileW.restype = wintypes.HANDLE

# Define SetNamedPipeHandleState
KERNEL32.SetNamedPipeHandleState.argtypes = [
    wintypes.HANDLE,
    ctypes.POINTER(wintypes.DWORD),
    ctypes.POINTER(wintypes.DWORD),
    ctypes.POINTER(wintypes.DWORD)
]
KERNEL32.SetNamedPipeHandleState.restype = wintypes.BOOL

logger = logging.getLogger(__name__)

class TestClient(Base):
    def __init__(self, name: str) -> None:
        super().__init__(Mode.MASTER)
        self.name = name

        pipe_path = f"\\\\.\\pipe\\{name}"

        handle = KERNEL32.CreateFileW(
            pipe_path,
            GENERIC_READ | GENERIC_WRITE,
            0,            # No sharing
            None,         # Default security
            OPEN_EXISTING,
            0,            # Default attributes
            None          # No template
        )

        if handle == INVALID_HANDLE_VALUE:
            raise PipeError(f"Pipe Open Failed [{KERNEL32.GetLastError()}]")

        self.handle = handle

        mode = wintypes.DWORD(PIPE_READMODE_MESSAGE)
        ret = KERNEL32.SetNamedPipeHandleState(
            self.handle,
            ctypes.byref(mode),
            None,
            None
        )

        if ret == 0:
            err = KERNEL32.GetLastError()
            self.close()
            raise PipeError(f"Pipe Set Mode Failed [{err}]")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.close()
        return False

    def sendRequest(self, command: str, data: FixedSpeedRequest | None = None, timeout: float = 5.0):
        """Sends a request and returns the decoded BridgeResponse."""
        if not self.alive:
            raise PipeError("Client is not connected")

        req = PipeRequest(command=command, data=data)
        encoded_bytes = msgspec.msgpack.encode(req)

        if not self.write(encoded_bytes):
            raise PipeError("Failed to write to pipe")

        start_time = time.time()
        while time.time() - start_time < timeout:
            raw_response = self.read()
            if raw_response:
                return msgspec.msgpack.decode(raw_response, type=BridgeResponse)
            time.sleep(0.01)

        raise PipeError(f"Timeout waiting for response after {timeout}s")

    def close(self) -> None:
        """Manually closes the handle since Base doesn't have a close method."""
        if self.handle and self.handle != INVALID_HANDLE_VALUE:
            KERNEL32.CloseHandle(self.handle)
            self.handle = None
