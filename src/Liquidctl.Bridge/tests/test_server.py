"""Unit tests for server module."""

import pytest
from unittest.mock import Mock, patch, MagicMock
import msgspec
from liquidctl_bridge.server import (
    setup_logging,
    process_command,
)
from liquidctl_bridge.models import (
    PipeRequest,
    FixedSpeedRequest,
    SpeedKwargs,
    BadRequestException,
)
from liquidctl_bridge.liquidctl_service import LiquidctlService
import logging


class TestSetupLogging:
    """Test setup_logging function."""

    @patch('logging.basicConfig')
    def test_setup_logging_default_level(self, mock_basic_config):
        """Should call basicConfig with INFO level by default."""
        setup_logging()
        mock_basic_config.assert_called_once()
        call_kwargs = mock_basic_config.call_args[1]
        assert call_kwargs['level'] == logging.INFO

    @patch('logging.basicConfig')
    def test_setup_logging_debug_level(self, mock_basic_config):
        """Should call basicConfig with DEBUG level."""
        setup_logging("DEBUG")
        mock_basic_config.assert_called_once()
        call_kwargs = mock_basic_config.call_args[1]
        assert call_kwargs['level'] == logging.DEBUG

    @patch('logging.basicConfig')
    def test_setup_logging_warning_level(self, mock_basic_config):
        """Should call basicConfig with WARNING level."""
        setup_logging("WARNING")
        mock_basic_config.assert_called_once()
        call_kwargs = mock_basic_config.call_args[1]
        assert call_kwargs['level'] == logging.WARNING

    @patch('logging.basicConfig')
    def test_setup_logging_invalid_level(self, mock_basic_config):
        """Should default to INFO for invalid level."""
        setup_logging("INVALID")
        mock_basic_config.assert_called_once()
        call_kwargs = mock_basic_config.call_args[1]
        assert call_kwargs['level'] == logging.INFO

    @patch('logging.basicConfig')
    def test_setup_logging_lowercase(self, mock_basic_config):
        """Should handle lowercase log levels."""
        setup_logging("debug")
        mock_basic_config.assert_called_once()
        call_kwargs = mock_basic_config.call_args[1]
        assert call_kwargs['level'] == logging.DEBUG


class TestProcessCommand:
    """Test process_command function."""

    @pytest.fixture
    def mock_service(self):
        """Create a mock LiquidctlService."""
        return Mock(spec=LiquidctlService)

    def test_process_command_get_statuses(self, mock_service):
        """Should call get_statuses on service."""
        mock_service.get_statuses.return_value = []
        request = PipeRequest(command="get.statuses")

        result = process_command(request, mock_service)

        mock_service.get_statuses.assert_called_once()
        assert result == []

    def test_process_command_set_fixed_speed(self, mock_service):
        """Should call set_fixed_speed with correct parameters."""
        request = PipeRequest(
            command="set.fixed_speed",
            data=FixedSpeedRequest(
                device_id=1, speed_kwargs=SpeedKwargs(channel="pump", duty=75)
            ),
        )

        result = process_command(request, mock_service)

        mock_service.set_fixed_speed.assert_called_once()
        # Check that device_id is correct
        call_args = mock_service.set_fixed_speed.call_args
        assert call_args[0][0] == 1
        # Check that speed_kwargs dict has correct values
        speed_kwargs = call_args[0][1]
        assert speed_kwargs["channel"] == "pump"
        assert speed_kwargs["duty"] == 75

    def test_process_command_set_fixed_speed_no_data(self, mock_service):
        """Should raise BadRequestException if no data provided."""
        request = PipeRequest(command="set.fixed_speed", data=None)

        with pytest.raises(BadRequestException, match="No data provided"):
            process_command(request, mock_service)

    def test_process_command_unknown_command(self, mock_service):
        """Should return None for unknown commands (match statement)."""
        request = PipeRequest(command="unknown.command")

        result = process_command(request, mock_service)

        # Python match statement returns None for no match
        assert result is None


class TestHandlePipeMessage:
    """Test handle_pipe_message function (conceptual tests)."""

    def test_handle_pipe_message_integration(self):
        """
        Integration test concept for handle_pipe_message.

        This would test the full pipeline:
        1. Reading from pipe
        2. Decoding message
        3. Processing command
        4. Encoding response
        5. Writing to pipe

        In practice, this requires mocking the pipe server or
        running an actual integration test with a pipe.
        """
        pass


class TestMain:
    """Test main function (conceptual tests)."""

    def test_main_argument_parsing(self):
        """
        Test that main parses command-line arguments correctly.

        This would test:
        1. Default log level is INFO
        2. --log-level argument is parsed
        3. Invalid log levels are handled

        In practice, this requires mocking sys.argv and testing
        the argparse configuration.
        """
        pass

    def test_main_server_lifecycle(self):
        """
        Test the main server lifecycle.

        This would test:
        1. LiquidctlService context manager is used
        2. Server context manager is used
        3. initialize_all is called
        4. Main loop runs while pipe is alive

        In practice, this requires integration testing or
        careful mocking of the context managers.
        """
        pass


class TestIntegration:
    """Integration tests that would require actual pipe communication."""

    def test_full_get_statuses_flow(self):
        """
        Full integration test for get.statuses command.

        Would test:
        1. Client sends get.statuses request
        2. Server processes and queries liquidctl
        3. Server returns device statuses
        4. Client receives and decodes response
        """
        pass

    def test_full_set_fixed_speed_flow(self):
        """
        Full integration test for set.fixed_speed command.

        Would test:
        1. Client sends set.fixed_speed request
        2. Server processes and calls liquidctl
        3. Device speed is set
        4. Server returns success
        """
        pass
