import sys
import time

import pytest

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="named pipes use Win32 APIs"
)

if sys.platform == "win32":
    from liquidctl_server import win32_pipe
    from liquidctl_server.models import PipeError
    from liquidctl_server.pipe_server import PipeServer


class _PipeClient:
    def __init__(self, pipe_name: str) -> None:
        deadline = time.monotonic() + 5.0
        while True:
            try:
                self.handle = win32_pipe.open_client_pipe(pipe_name)
                return
            except PipeError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.05)

    def request(self, payload: bytes) -> bytes:
        win32_pipe.write_message(self.handle, payload)
        return win32_pipe.read_message(self.handle)

    def close(self) -> None:
        if self.handle is not None:
            win32_pipe.close(self.handle)
            self.handle = None


def _echo_upper(request: bytes) -> bytes:
    return request.upper()


def _echo(request: bytes) -> bytes:
    return request


class TestPipeServer:
    def test_request_response_roundtrip(self):
        server = PipeServer("LiquidCtlPipeTest_RT", _echo_upper)
        server.start()
        client = None
        try:
            client = _PipeClient(server.name)
            assert client.request(b'{"command":"x"}') == b'{"COMMAND":"X"}'
        finally:
            if client is not None:
                client.close()
            server.stop()

    def test_multiple_requests_on_one_connection(self):
        server = PipeServer("LiquidCtlPipeTest_MULTI", _echo_upper)
        server.start()
        client = None
        try:
            client = _PipeClient(server.name)
            assert client.request(b"first") == b"FIRST"
            assert client.request(b"second") == b"SECOND"
        finally:
            if client is not None:
                client.close()
            server.stop()

    def test_new_client_can_connect_after_disconnect(self):
        server = PipeServer("LiquidCtlPipeTest_RECONNECT", _echo_upper)
        server.start()
        second = None
        try:
            first = _PipeClient(server.name)
            assert first.request(b"one") == b"ONE"
            first.close()

            second = _PipeClient(server.name)
            assert second.request(b"two") == b"TWO"
        finally:
            if second is not None:
                second.close()
            server.stop()

    def test_stop_unblocks_waiting_server_thread(self):
        server = PipeServer("LiquidCtlPipeTest_STOP", _echo_upper)
        server.start()

        server.stop()

        assert not server._thread.is_alive()

    def test_stop_while_client_connected(self):
        server = PipeServer("LiquidCtlPipeTest_STOPCONN", _echo_upper)
        server.start()
        client = _PipeClient(server.name)
        try:
            server.stop()
            assert not server._thread.is_alive()
        finally:
            client.close()

    def test_large_payload_survives_roundtrip(self):
        payload = b"x" * 100_000
        server = PipeServer("LiquidCtlPipeTest_LARGE", _echo)
        server.start()
        client = None
        try:
            client = _PipeClient(server.name)
            assert client.request(payload) == payload
        finally:
            if client is not None:
                client.close()
            server.stop()

    def test_handler_crash_keeps_server_alive_for_next_client(self):
        calls = []

        def _raise_once(request: bytes) -> bytes:
            calls.append(request)
            if len(calls) == 1:
                raise RuntimeError("boom")
            return _echo_upper(request)

        server = PipeServer("LiquidCtlPipeTest_CRASH", _raise_once)
        server.start()
        first, second = None, None
        try:
            first = _PipeClient(server.name)
            win32_pipe.write_message(first.handle, b"first")
            first.close()
            first = None

            assert server.is_alive()

            second = _PipeClient(server.name)
            assert second.request(b"second") == b"SECOND"
        finally:
            if first is not None:
                first.close()
            if second is not None:
                second.close()
            server.stop()
