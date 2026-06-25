import sys
import time

import pytest

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="pipe_server uses Win32 named pipe APIs"
)

if sys.platform == "win32":
    import ctypes
    from ctypes import wintypes

    from liquidctl_server.models import PipeError
    from liquidctl_server.pipe_server import Base, Server

    _GENERIC_READ = 0x80000000
    _GENERIC_WRITE = 0x40000000
    _OPEN_EXISTING = 3
    _PIPE_READMODE_MESSAGE = 0x00000002
    _INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value

    # restype must be HANDLE (pointer-sized): the ctypes default of c_int would
    # truncate the returned 64-bit pipe handle on Win64.
    ctypes.windll.kernel32.CreateFileW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    ]
    ctypes.windll.kernel32.CreateFileW.restype = wintypes.HANDLE
    ctypes.windll.kernel32.SetNamedPipeHandleState.restype = wintypes.BOOL


class _PipeClient:
    def __init__(self, pipe_path: str) -> None:
        self._k32 = ctypes.windll.kernel32
        self.handle = self._connect(pipe_path)

    def _connect(self, pipe_path: str) -> int:
        deadline = time.monotonic() + 5.0
        while time.monotonic() < deadline:
            handle = self._k32.CreateFileW(
                pipe_path,
                _GENERIC_READ | _GENERIC_WRITE,
                0,
                None,
                _OPEN_EXISTING,
                0,
                None,
            )
            if handle != _INVALID_HANDLE_VALUE:
                mode = wintypes.DWORD(_PIPE_READMODE_MESSAGE)
                self._k32.SetNamedPipeHandleState(
                    handle, ctypes.byref(mode), None, None
                )
                return handle
            time.sleep(0.05)
        raise AssertionError(f"could not connect to {pipe_path}")

    def write(self, payload: bytes) -> None:
        written = wintypes.DWORD(0)
        self._k32.WriteFile(
            self.handle, payload, len(payload), ctypes.byref(written), None
        )

    def read(self, size: int = 65536) -> bytes:
        buffer = ctypes.create_string_buffer(size)
        read_bytes = wintypes.DWORD(0)
        self._k32.ReadFile(self.handle, buffer, size, ctypes.byref(read_bytes), None)
        return buffer.raw[: read_bytes.value]

    def close(self) -> None:
        if self.handle:
            ctypes.windll.kernel32.CloseHandle(self.handle)
            self.handle = None


class TestBaseWithoutConnection:
    def test_fresh_base_is_not_alive(self):
        assert Base().alive is False

    def test_canread_false_when_not_alive(self):
        assert Base().canread() is False

    def test_read_none_when_not_alive(self):
        assert Base().read() is None

    def test_write_raises_when_not_alive(self):
        with pytest.raises(PipeError):
            Base().write(b"data")


def _wait_until(predicate, timeout: float = 3.0) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(0.05)
    return False


class TestServerLoopback:
    def test_client_to_server_roundtrip(self):
        server = Server(name="LiquidCtlPipeTest_RX")
        client = None
        try:
            client = _PipeClient(server.pipe_path)
            assert _wait_until(lambda: server.alive)

            client.write(b'{"command":"get.statuses"}')
            assert _wait_until(server.canread)

            assert server.read() == b'{"command":"get.statuses"}'
        finally:
            server.close()
            if client is not None:
                client.close()

    def test_server_to_client_roundtrip(self):
        server = Server(name="LiquidCtlPipeTest_TX")
        client = None
        try:
            client = _PipeClient(server.pipe_path)
            assert _wait_until(lambda: server.alive)

            assert server.write(b'{"status":"success"}') is True
            assert client.read() == b'{"status":"success"}'
        finally:
            server.close()
            if client is not None:
                client.close()

    def test_context_manager_closes_server(self):
        with Server(name="LiquidCtlPipeTest_CM") as server:
            assert server.server_thread.is_alive()
        assert server.shutdown_event.is_set()
