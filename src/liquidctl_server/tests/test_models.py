import msgspec

from liquidctl_server.models import PipeRequest


class TestPipeRequest:
    def test_command_only_defaults_data_to_null(self):
        req = PipeRequest(command="get.statuses")
        assert req.command == "get.statuses"
        assert bytes(req.data) == b"null"

    def test_with_explicit_data(self):
        req = PipeRequest(
            command="set.fixed_speed", data=msgspec.Raw(b'{"device_id": 1}')
        )
        assert b"device_id" in bytes(req.data)
