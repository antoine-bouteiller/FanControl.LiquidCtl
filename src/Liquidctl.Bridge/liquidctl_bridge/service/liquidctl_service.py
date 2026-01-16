import logging
from concurrent.futures import TimeoutError as FuturesTimeoutError
from typing import Dict, List, Optional, Tuple, Union

import liquidctl
from liquidctl.driver.base import BaseDriver

from liquidctl_bridge.models import (
    BadRequestException,
    DeviceStatus,
    LiquidctlException,
    StatusValue,
)
from liquidctl_bridge.service.config import (
    DEVICE_OPERATION_TIMEOUT,
    DEVICE_STATUS_TIMEOUT,
    MAX_INIT_RETRIES,
)
from liquidctl_bridge.service.executor import DeviceExecutor
from liquidctl_bridge.service.formatters import format_status_key

logger = logging.getLogger(__name__)


class LiquidctlService:
    """Service for managing liquidctl devices with thread-safe operations."""

    def __init__(self) -> None:
        self.devices: Dict[int, BaseDriver] = {}
        self.device_status_cache: Dict[int, List[StatusValue]] = {}
        self.previous_duty: Dict[str, Union[str, int, None]] = {}
        self._executor: DeviceExecutor = DeviceExecutor()

    def __enter__(self) -> "LiquidctlService":
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self.shutdown()
        if exc_value is not None:
            logger.error(f"Exception during service context: {exc_value}", exc_info=True)

    def initialize_all(self) -> None:
        """Find and initialize all liquidctl devices with retry logic."""
        for attempt in range(MAX_INIT_RETRIES):
            try:
                self._find_devices()
                return
            except Exception as e:
                if attempt < MAX_INIT_RETRIES - 1:
                    logger.warning(
                        f"Device initialization attempt {attempt + 1}/{MAX_INIT_RETRIES} "
                        f"failed: {e}"
                    )
                else:
                    logger.error(
                        f"Failed to initialize devices after {MAX_INIT_RETRIES} attempts",
                        exc_info=True,
                    )

    def _find_devices(self) -> None:
        """Find all liquidctl devices and connect to them."""
        try:
            found_devices: List[BaseDriver] = list(liquidctl.find_liquidctl_devices())
        except ValueError:
            logger.info("No Liquidctl devices detected")
            return

        if not found_devices:
            logger.info("No Liquidctl devices detected")
            return

        self._executor.set_number_of_devices(len(found_devices))

        for index, lc_device in enumerate(found_devices):
            device_id = index + 1
            try:
                self._connect_device(device_id, lc_device)
                self.devices[device_id] = lc_device
            except Exception as e:
                logger.error(
                    f"Failed to connect device #{device_id} ({lc_device.description}): {e}"
                )

        device_names = [d.description for d in self.devices.values()]
        logger.info(f"Devices initialized: {device_names}")

    def _connect_device(self, device_id: int, lc_device: BaseDriver) -> None:
        """Connect and initialize a single device."""
        try:
            connect_job = self._executor.submit(device_id, lc_device.connect)
            connect_job.result(timeout=DEVICE_OPERATION_TIMEOUT)

            init_job = self._executor.submit(device_id, lc_device.initialize)
            init_job.result(timeout=DEVICE_OPERATION_TIMEOUT)

        except RuntimeError as err:
            if "already open" in str(err):
                logger.warning(f"Device #{device_id} already connected")
            else:
                raise LiquidctlException(f"Device connection error: {err}") from err

    def get_statuses(self) -> List[DeviceStatus]:
        """Get status for all devices."""
        if not self.devices:
            return []

        statuses: List[DeviceStatus] = []
        for device_id, lc_device in self.devices.items():
            status = self._get_current_or_cached_device_status(device_id, lc_device)
            if status is not None:
                statuses.append(status)

        return statuses

    def _get_current_or_cached_device_status(
        self, device_id: int, lc_device: BaseDriver
    ) -> Optional[DeviceStatus]:
        """Get status for a single device, falling back to cache on timeout."""
        status_job = self._executor.submit(device_id, lc_device.get_status)
        try:
            raw_status = status_job.result(timeout=DEVICE_STATUS_TIMEOUT)
            status_values = self._stringify_status(raw_status)
            self.device_status_cache[device_id] = status_values

            return DeviceStatus(
                id=device_id,
                description=lc_device.description,
                bus=lc_device.bus,
                address=lc_device.address,
                status=status_values,
            )

        except FuturesTimeoutError:
            return self._handle_status_timeout(device_id, lc_device)

        except Exception as e:
            logger.warning(f"Error getting status for device #{device_id}: {e}")
            return self._build_status_from_cache(device_id, lc_device)

        finally:
            status_job.cancel()

    def _handle_status_timeout(
        self, device_id: int, lc_device: BaseDriver
    ) -> Optional[DeviceStatus]:
        """Handle status timeout with async retry or cache fallback."""
        cached = self._build_status_from_cache(device_id, lc_device)

        if self._executor.device_queue_empty(device_id):
            async_job = self._executor.submit(
                device_id, self._long_async_status_request, device_id=device_id
            )

            if cached is not None:
                return cached

            try:
                return async_job.result(timeout=DEVICE_OPERATION_TIMEOUT)
            except FuturesTimeoutError:
                logger.error(f"Status request timed out for device #{device_id}")
                return None
            finally:
                async_job.cancel()

        return cached

    def _long_async_status_request(self, device_id: int) -> Optional[DeviceStatus]:
        """Long-running async status request that updates the cache."""
        lc_device = self.devices[device_id]
        raw_status = lc_device.get_status()
        status_values = self._stringify_status(raw_status)
        self.device_status_cache[device_id] = status_values

        return DeviceStatus(
            id=device_id,
            description=lc_device.description,
            bus=lc_device.bus,
            address=lc_device.address,
            status=status_values,
        )

    def _build_status_from_cache(
        self, device_id: int, lc_device: BaseDriver
    ) -> Optional[DeviceStatus]:
        """Build a DeviceStatus from cached values."""
        cached = self.device_status_cache.get(device_id)
        if cached is None:
            return None

        return DeviceStatus(
            id=device_id,
            description=lc_device.description,
            bus=lc_device.bus,
            address=lc_device.address,
            status=cached,
        )

    def set_fixed_speed(
        self, device_id: int, speed_kwargs: Dict[str, Union[str, int]]
    ) -> None:
        """Set fixed speed for a device channel."""
        if device_id not in self.devices:
            raise BadRequestException(f"Device with id:{device_id} not found")

        channel = speed_kwargs.get("channel")
        duty = speed_kwargs.get("duty")
        cache_key = f"{device_id}_{channel}"

        if self.previous_duty.get(cache_key) == duty:
            return

        try:
            lc_device = self.devices[device_id]
            speed_job = self._executor.submit(
                device_id, lc_device.set_fixed_speed, **speed_kwargs
            )
            speed_job.result(timeout=DEVICE_OPERATION_TIMEOUT)
            self.previous_duty[cache_key] = duty

        except FuturesTimeoutError:
            logger.error(f"Timeout setting speed for device #{device_id}")
        except Exception as e:
            logger.error(f"Error setting fixed speed for device #{device_id}: {e}")

    def disconnect_all(self) -> None:
        """Disconnect all devices."""
        for device_id, lc_device in self.devices.items():
            try:
                disconnect_job = self._executor.submit(device_id, lc_device.disconnect)
                disconnect_job.result(timeout=DEVICE_OPERATION_TIMEOUT)
            except Exception as e:
                logger.warning(f"Error disconnecting device #{device_id}: {e}")

    def shutdown(self) -> None:
        """Disconnect all devices and cleanup resources."""
        self.disconnect_all()
        self._executor.shutdown()
        self.devices.clear()
        self.device_status_cache.clear()
        self.previous_duty.clear()

    @staticmethod
    def _stringify_status(
        statuses: Union[List[Tuple[str, Union[str, int, float], str]], None],
    ) -> List[StatusValue]:
        """Convert raw liquidctl status to StatusValue list."""
        if statuses is None:
            return []

        result: List[StatusValue] = []
        for status in statuses:
            key = status[0]

            try:
                value = float(status[1])
            except (ValueError, TypeError):
                value = None

            result.append(
                StatusValue(
                    key=format_status_key(key),
                    value=value,
                    unit=str(status[2]),
                )
            )

        return result
