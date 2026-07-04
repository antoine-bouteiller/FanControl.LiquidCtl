import ctypes
from ctypes import wintypes

from liquidctl_server.models import PipeError

# use_last_error=True makes ctypes capture GetLastError into a thread-local
# right after each call; calling KERNEL32.GetLastError() manually is racy
# because the interpreter may issue Win32 calls in between.
KERNEL32 = ctypes.WinDLL("kernel32", use_last_error=True)

PIPE_ACCESS_DUPLEX = 0x00000003
PIPE_TYPE_MESSAGE = 0x00000004
PIPE_READMODE_MESSAGE = 0x00000002
PIPE_WAIT = 0x00000000
PIPE_UNLIMITED_INSTANCES = 255
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
BUFFER_SIZE = 65536

ERROR_MORE_DATA = 234
ERROR_PIPE_CONNECTED = 535
INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value

# Explicit signatures: the ctypes default return type (c_int) would truncate
# 64-bit pipe handles on Win64.
KERNEL32.CreateNamedPipeW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.LPVOID,
]
KERNEL32.CreateNamedPipeW.restype = wintypes.HANDLE

KERNEL32.CreateFileW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.LPVOID,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.HANDLE,
]
KERNEL32.CreateFileW.restype = wintypes.HANDLE

KERNEL32.SetNamedPipeHandleState.argtypes = [
    wintypes.HANDLE,
    ctypes.POINTER(wintypes.DWORD),
    ctypes.POINTER(wintypes.DWORD),
    ctypes.POINTER(wintypes.DWORD),
]
KERNEL32.SetNamedPipeHandleState.restype = wintypes.BOOL

KERNEL32.ConnectNamedPipe.argtypes = [wintypes.HANDLE, wintypes.LPVOID]
KERNEL32.ConnectNamedPipe.restype = wintypes.BOOL

KERNEL32.ReadFile.argtypes = [
    wintypes.HANDLE,
    wintypes.LPVOID,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
    wintypes.LPVOID,
]
KERNEL32.ReadFile.restype = wintypes.BOOL

KERNEL32.WriteFile.argtypes = [
    wintypes.HANDLE,
    wintypes.LPCVOID,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
    wintypes.LPVOID,
]
KERNEL32.WriteFile.restype = wintypes.BOOL

KERNEL32.CancelIoEx.argtypes = [wintypes.HANDLE, wintypes.LPVOID]
KERNEL32.CancelIoEx.restype = wintypes.BOOL

KERNEL32.CloseHandle.argtypes = [wintypes.HANDLE]
KERNEL32.CloseHandle.restype = wintypes.BOOL


def pipe_path(name: str) -> str:
    return f"\\\\.\\pipe\\{name}"


def create_server_pipe(name: str) -> int:
    handle = KERNEL32.CreateNamedPipeW(
        pipe_path(name),
        PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
        PIPE_UNLIMITED_INSTANCES,
        BUFFER_SIZE,
        BUFFER_SIZE,
        0,
        None,
    )
    if handle == INVALID_HANDLE_VALUE:
        raise PipeError(f"CreateNamedPipe failed [{ctypes.get_last_error()}]")
    return handle


def wait_for_client(handle: int) -> None:
    if not KERNEL32.ConnectNamedPipe(handle, None):
        err = ctypes.get_last_error()
        if err != ERROR_PIPE_CONNECTED:
            raise PipeError(f"ConnectNamedPipe failed [{err}]")


def open_client_pipe(name: str) -> int:
    handle = KERNEL32.CreateFileW(
        pipe_path(name),
        GENERIC_READ | GENERIC_WRITE,
        0,
        None,
        OPEN_EXISTING,
        0,
        None,
    )
    if handle == INVALID_HANDLE_VALUE:
        raise PipeError(f"CreateFile failed [{ctypes.get_last_error()}]")

    mode = wintypes.DWORD(PIPE_READMODE_MESSAGE)
    if not KERNEL32.SetNamedPipeHandleState(handle, ctypes.byref(mode), None, None):
        err = ctypes.get_last_error()
        close(handle)
        raise PipeError(f"SetNamedPipeHandleState failed [{err}]")
    return handle


def read_message(handle: int) -> bytes:
    chunks = []
    while True:
        buffer = ctypes.create_string_buffer(BUFFER_SIZE)
        read = wintypes.DWORD(0)
        ok = KERNEL32.ReadFile(handle, buffer, BUFFER_SIZE, ctypes.byref(read), None)
        err = ctypes.get_last_error()
        if not ok and err != ERROR_MORE_DATA:
            raise PipeError(f"ReadFile failed [{err}]")
        chunks.append(buffer.raw[: read.value])
        if ok:
            return b"".join(chunks)


def write_message(handle: int, message: bytes) -> None:
    written = wintypes.DWORD(0)
    if not KERNEL32.WriteFile(
        handle, message, len(message), ctypes.byref(written), None
    ):
        raise PipeError(f"WriteFile failed [{ctypes.get_last_error()}]")


def close(handle: int) -> None:
    # CancelIoEx unblocks any ReadFile/ConnectNamedPipe pending on another
    # thread; CloseHandle alone is not guaranteed to interrupt them.
    KERNEL32.CancelIoEx(handle, None)
    KERNEL32.CloseHandle(handle)
