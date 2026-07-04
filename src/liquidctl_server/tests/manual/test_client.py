import msgspec

from liquidctl_server import win32_pipe
from liquidctl_server.models import BridgeResponse, PipeRequest


class TestClient:
    def __init__(self, name: str) -> None:
        self.name = name
        self.handle = win32_pipe.open_client_pipe(name)

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.close()
        return False

    def sendRequest(self, command: str, data=None) -> BridgeResponse:
        # data is decoded per-command on the server, so pre-encode it as Raw.
        if data is not None:
            req = PipeRequest(
                command=command, data=msgspec.Raw(msgspec.json.encode(data))
            )
        else:
            req = PipeRequest(command=command)
        win32_pipe.write_message(self.handle, msgspec.json.encode(req))
        raw_response = win32_pipe.read_message(self.handle)
        return msgspec.json.decode(raw_response, type=BridgeResponse)

    def close(self) -> None:
        if self.handle is not None:
            win32_pipe.close(self.handle)
            self.handle = None
