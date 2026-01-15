import ctypes
import logging
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
        # Base init sets self.handle = None
        super().__init__(Mode.MASTER)
        self.name = name

        pipe_path = f"\\\\.\\pipe\\{name}"

        # 1. Open the Pipe
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

        # Assigning handle automatically makes self.alive (property) return True
        self.handle = handle

        # 2. Set Pipe Mode to MESSAGE
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

    def sendRequest(self, command: str, data: FixedSpeedRequest | None = None):
        """Sends a request and returns the decoded BridgeResponse."""
        if not self.alive:
            raise PipeError("Client is not connected")

        # 1. Encode Request -> MessagePack Bytes
        req = PipeRequest(command=command, data=data)
        encoded_bytes = msgspec.msgpack.encode(req)

        # 2. Write (uses Base.write)
        if not self.write(encoded_bytes):
            raise PipeError("Failed to write to pipe")

        # 3. Read Response (uses Base.read)
        raw_response = self.read()

        if not raw_response:
            return None

        # 4. Decode Response -> BridgeResponse Object
        return msgspec.msgpack.decode(raw_response, type=BridgeResponse)

    def close(self) -> None:
        """Manually closes the handle since Base doesn't have a close method."""
        if self.handle and self.handle != INVALID_HANDLE_VALUE:
            KERNEL32.CloseHandle(self.handle)
            self.handle = None
