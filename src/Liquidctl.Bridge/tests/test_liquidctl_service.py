"""Unit tests for liquidctl_service module."""

import pytest
from unittest.mock import Mock, MagicMock, patch
from liquidctl_bridge.liquidctl_service import LiquidctlService, _formatString
from liquidctl_bridge.models import (
    BadRequestException,
    LiquidctlException,
    StatusValue,
)


class TestFormatString:
    """Test the _formatString function."""

    def test_format_fan_with_number(self):
        """Should format 'fan 1' to 'fan1'."""
        assert _formatString("fan 1") == "fan1"
        assert _formatString("Fan 2") == "fan2"
        assert _formatString("FAN 3") == "fan3"

    def test_format_fan_with_duty(self):
        """Should strip 'duty' from fan strings."""
        assert _formatString("fan 1 duty") == "fan1"
        assert _formatString("fan 2 duty") == "fan2"

    def test_format_generic_duty(self):
        """Should strip 'duty' from generic strings."""
        assert _formatString("pump duty") == "pump"
        assert _formatString("led duty") == "led"

    def test_format_no_duty(self):
        """Should return lowercase string without modification."""
        assert _formatString("pump speed") == "pump speed"
        assert _formatString("Liquid Temperature") == "liquid temperature"

    def test_format_fan_with_extra_text(self):
        """Should handle fan patterns with additional text."""
        assert _formatString("fan 1 speed") == "fan1 speed"


class TestLiquidctlService:
    """Test LiquidctlService class."""

    @pytest.fixture
    def service(self):
        """Create a LiquidctlService instance."""
        return LiquidctlService()

    def test_init(self, service):
        """Should initialize with empty devices dict."""
        assert service.devices == {}
        assert service.previous_duty == {}

    def test_context_manager_enter(self, service):
        """Should return self on enter."""
        with service as svc:
            assert svc is service

    def test_context_manager_exit(self, service):
        """Should call shutdown on exit."""
        with patch.object(service, "shutdown") as mock_shutdown:
            with service:
                pass
        mock_shutdown.assert_called_once()

    def test_initialize_all_no_devices(self, service):
        """Should handle no devices found gracefully."""
        with patch("liquidctl_bridge.liquidctl_service.liquidctl.find_liquidctl_devices") as mock_find:
            mock_find.return_value = []
            service.initialize_all()
            assert service.devices == {}

    def test_initialize_all_with_devices(self, service):
        """Should initialize found devices."""
        mock_device = Mock()
        mock_device.description = "NZXT Kraken X53"
        mock_device.bus = "hid"
        mock_device.address = "1234"

        with patch("liquidctl_bridge.liquidctl_service.liquidctl.find_liquidctl_devices") as mock_find:
            mock_find.return_value = [mock_device]
            service.initialize_all()

            assert len(service.devices) == 1
            assert service.devices[1] == mock_device
            mock_device.connect.assert_called_once()
            mock_device.initialize.assert_called_once()

    def test_initialize_all_handles_exception(self, service):
        """Should log error if initialization fails."""
        with patch("liquidctl_bridge.liquidctl_service.liquidctl.find_liquidctl_devices") as mock_find:
            mock_find.side_effect = Exception("USB error")
            # Should not raise, just log
            service.initialize_all()
            assert service.devices == {}

    def test_get_statuses_no_devices(self, service):
        """Should raise exception if devices not initialized."""
        with pytest.raises(LiquidctlException, match="initialize the devices first"):
            service.get_statuses()

    def test_get_statuses_with_devices(self, service):
        """Should return device statuses."""
        mock_device = Mock()
        mock_device.description = "NZXT Kraken X53"
        mock_device.bus = "hid"
        mock_device.address = "1234"
        mock_device.get_status.return_value = [
            ("Liquid temperature", 28.5, "°C"),
            ("pump speed", 2500, "rpm"),
            ("pump duty", 50, "%"),
        ]

        service.devices[1] = mock_device
        statuses = service.get_statuses()

        assert len(statuses) == 1
        device_status = statuses[0]
        assert device_status.id == 1
        assert device_status.description == "NZXT Kraken X53"
        assert device_status.bus == "hid"
        assert device_status.address == "1234"
        assert len(device_status.status) == 3

    def test_get_statuses_filters_control_mode(self, service):
        """Should filter out 'control mode' from status."""
        mock_device = Mock()
        mock_device.description = "NZXT Kraken X53"
        mock_device.bus = "hid"
        mock_device.address = "1234"
        mock_device.get_status.return_value = [
            ("Liquid temperature", 28.5, "°C"),
            ("pump control mode", "PWM", ""),
        ]

        service.devices[1] = mock_device
        statuses = service.get_statuses()

        # Control mode should be filtered out
        assert len(statuses[0].status) == 1
        assert statuses[0].status[0].key == "liquid temperature"

    def test_get_statuses_handles_exception(self, service):
        """Should return empty list on exception."""
        mock_device = Mock()
        mock_device.get_status.side_effect = Exception("Device error")
        service.devices[1] = mock_device

        statuses = service.get_statuses()
        assert statuses == []

    def test_set_fixed_speed_device_not_found(self, service):
        """Should raise BadRequestException if device not found."""
        with pytest.raises(BadRequestException, match="Device with id:99 not found"):
            service.set_fixed_speed(99, {"channel": "pump", "duty": 50})

    def test_set_fixed_speed_success(self, service):
        """Should set fixed speed on device."""
        mock_device = Mock()
        service.devices[1] = mock_device

        service.set_fixed_speed(1, {"channel": "pump", "duty": 50})

        mock_device.set_fixed_speed.assert_called_once_with(channel="pump", duty=50)
        assert service.previous_duty["1_pump"] == 50

    def test_set_fixed_speed_skip_if_same_duty(self, service):
        """Should skip setting if duty hasn't changed."""
        mock_device = Mock()
        service.devices[1] = mock_device
        service.previous_duty["1_pump"] = 50

        service.set_fixed_speed(1, {"channel": "pump", "duty": 50})

        # Should not call set_fixed_speed since duty is the same
        mock_device.set_fixed_speed.assert_not_called()

    def test_set_fixed_speed_handles_exception(self, service):
        """Should log error if set_fixed_speed fails."""
        mock_device = Mock()
        mock_device.set_fixed_speed.side_effect = Exception("Device error")
        service.devices[1] = mock_device

        # Should not raise, just log
        service.set_fixed_speed(1, {"channel": "pump", "duty": 50})

    def test_shutdown(self, service):
        """Should disconnect all devices and clear the dict."""
        mock_device1 = Mock()
        mock_device2 = Mock()
        service.devices[1] = mock_device1
        service.devices[2] = mock_device2

        service.shutdown()

        mock_device1.disconnect.assert_called_once()
        mock_device2.disconnect.assert_called_once()
        assert service.devices == {}

    def test_stringify_status_none(self):
        """Should return empty list for None status."""
        result = LiquidctlService._stringify_status(None)
        assert result == []

    def test_stringify_status_with_values(self):
        """Should convert tuples to StatusValue objects."""
        status_tuples = [
            ("Liquid temperature", 28.5, "°C"),
            ("pump speed", 2500, "rpm"),
            ("pump duty", 50, "%"),
        ]

        result = LiquidctlService._stringify_status(status_tuples)

        assert len(result) == 3
        assert all(isinstance(s, StatusValue) for s in result)
        assert result[0].key == "liquid temperature"
        assert result[0].value == 28.5
        assert result[0].unit == "°C"

    def test_stringify_status_converts_to_float(self):
        """Should convert numeric values to float."""
        status_tuples = [
            ("pump speed", 2500, "rpm"),  # int
            ("Liquid temperature", 28.5, "°C"),  # float
        ]

        result = LiquidctlService._stringify_status(status_tuples)

        assert isinstance(result[0].value, float)
        assert result[0].value == 2500.0
        assert isinstance(result[1].value, float)
        assert result[1].value == 28.5
