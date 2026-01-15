import ctypes
import logging
import threading
import time
from ctypes import wintypes
from typing import Optional
from liquidctl_bridge.models import Mode, PipeError

# --- Win32 API Definitions ---
KERNEL32 = ctypes.windll.kernel32

PIPE_ACCESS_DUPLEX = 0x00000003
PIPE_TYPE_MESSAGE = 0x00000004
PIPE_READMODE_MESSAGE = 0x00000002
PIPE_WAIT = 0x00000000
PIPE_UNLIMITED_INSTANCES = 255

# Win32 Error Codes
ERROR_PIPE_BUSY = 231
ERROR_PIPE_CONNECTED = 535
ERROR_BROKEN_PIPE = 109
INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value

# Define argument/return types to prevent ctypes guessing errors
KERNEL32.CreateNamedPipeW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
    wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
    wintypes.DWORD, wintypes.LPVOID
]
KERNEL32.CreateNamedPipeW.restype = wintypes.HANDLE

KERNEL32.ConnectNamedPipe.argtypes = [wintypes.HANDLE, wintypes.LPVOID]
KERNEL32.ConnectNamedPipe.restype = wintypes.BOOL

KERNEL32.ReadFile.argtypes = [
    wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID
]
KERNEL32.ReadFile.restype = wintypes.BOOL

KERNEL32.WriteFile.argtypes = [
    wintypes.HANDLE, wintypes.LPCVOID, wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID
]
KERNEL32.WriteFile.restype = wintypes.BOOL

KERNEL32.PeekNamedPipe.argtypes = [
    wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(wintypes.DWORD),
    ctypes.POINTER(wintypes.DWORD)
]
KERNEL32.PeekNamedPipe.restype = wintypes.BOOL

KERNEL32.CloseHandle.argtypes = [wintypes.HANDLE]
KERNEL32.CloseHandle.restype = wintypes.BOOL

logger = logging.getLogger(__name__)


class Base:
    def __init__(self, mode: int = Mode.SLAVE):
        self.mode = mode
        self.handle: Optional[int] = None
        self._io_lock = threading.Lock()

    @property
    def alive(self) -> bool:
        return self.handle is not None and self.handle != INVALID_HANDLE_VALUE

    def canread(self) -> bool:
        if not self.alive:
            return False

        avail_bytes = wintypes.DWORD(0)

        with self._io_lock:
            if not self.alive:
                return False
            success = KERNEL32.PeekNamedPipe(
                self.handle, None, 0, None,
                ctypes.byref(avail_bytes), None
            )

        if not success:
            # If Peek fails, the pipe is likely broken/closed
            return False

        return avail_bytes.value > 0

    def read(self) -> Optional[bytes]:
        """Reads data if available. Returns None if no data or pipe dead."""
        if not self.canread():
            return None

        avail_bytes = wintypes.DWORD(0)

        with self._io_lock:
            if not self.alive:
                return None

            # Peek again to get exact size
            KERNEL32.PeekNamedPipe(
                self.handle, None, 0, None,
                ctypes.byref(avail_bytes), None
            )

            if avail_bytes.value == 0:
                return None

            buffer = ctypes.create_string_buffer(avail_bytes.value)
            read_bytes = wintypes.DWORD(0)

            success = KERNEL32.ReadFile(
                self.handle,
                buffer,
                len(buffer),
                ctypes.byref(read_bytes),
                None
            )

            if not success:
                return None

            return buffer.raw[:read_bytes.value]

    def write(self, message: bytes) -> bool:
        if not self.alive:
            raise PipeError("Pipe is dead!")

        written = wintypes.DWORD(0)

        with self._io_lock:
            if not self.alive:
                raise PipeError("Pipe is dead!")

            success = KERNEL32.WriteFile(
                self.handle,
                message,
                len(message),
                ctypes.byref(written),
                None
            )

        if not success:
            return False

        return True


class Server(Base):
    def __init__(self, name: str) -> None:
        super().__init__(Mode.SLAVE)
        self.name = name
        self.pipe_path = f"\\\\.\\pipe\\{self.name}"

        self.shutdown_event = threading.Event()
        self.server_thread = threading.Thread(target=self.serverentry, daemon=True)
        self.server_thread.start()

    def __enter__(self):
        logger.info("Starting Liquidctl Bridge Server")
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        if exc_type is KeyboardInterrupt:
            logger.info("KeyboardInterrupt detected, cleaning up Server...")
        elif exc_value:
            logger.error(f"Error: {exc_value}", exc_info=traceback)
        self.close()
        return False  # Propagate exceptions if any

    def close_connection(self) -> None:
        """Safely closes the underlying Win32 handle."""
        with self._io_lock:
            if self.handle:
                KERNEL32.CloseHandle(self.handle)
                self.handle = None

    def close(self) -> None:
        """Signal shutdown and cleanup."""
        self.shutdown_event.set()
        self.close_connection()
        if self.server_thread.is_alive():
            self.server_thread.join(timeout=1.0)

    def serverentry(self) -> None:
        logger.info(f"Starting Named Pipe server thread for: {self.name}")

        while not self.shutdown_event.is_set():
            nph = KERNEL32.CreateNamedPipeW(
                self.pipe_path,
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                PIPE_UNLIMITED_INSTANCES,
                65536,  # Out buffer
                65536,  # In buffer
                0,      # Default timeout
                None
            )

            if nph == INVALID_HANDLE_VALUE:
                logger.error(f"Failed CreateNamedPipe. Err: {KERNEL32.GetLastError()}")
                time.sleep(2)
                continue

            logger.debug("Waiting for client connection...")
            connected = KERNEL32.ConnectNamedPipe(nph, None)

            if not connected and KERNEL32.GetLastError() == ERROR_PIPE_CONNECTED:
                connected = True

            if connected or KERNEL32.GetLastError() == ERROR_PIPE_BUSY:
                logger.info("Client connected")

                with self._io_lock:
                    self.handle = nph

                while not self.shutdown_event.is_set():
                    if not self.canread():
                        if KERNEL32.GetLastError() == ERROR_BROKEN_PIPE:
                             break

                        time.sleep(0.1)
                    else:
                        time.sleep(0.1)

                logger.info("Client disconnected or shutdown triggered.")
                self.close_connection()
            else:
                KERNEL32.CloseHandle(nph)
                time.sleep(0.5)
