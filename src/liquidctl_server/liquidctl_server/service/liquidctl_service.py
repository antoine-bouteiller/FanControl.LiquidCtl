import logging
from concurrent.futures import TimeoutError as FuturesTimeoutError
from typing import Dict, List, Optional, Tuple, Union

import liquidctl
from liquidctl.driver.base import BaseDriver
from liquidctl.driver.commander_pro import CommanderPro
from liquidctl.driver.hydro_platinum import HydroPlatinum
from liquidctl.driver.smart_device import SmartDevice, SmartDevice2

try:
    from liquidctl.driver.aquacomputer import Aquacomputer
except ImportError:
    Aquacomputer = None

try:
    from liquidctl.driver.commander_core import CommanderCore
except ImportError:
    CommanderCore = None

try:
    from liquidctl.driver.smart_device import H1V2
except ImportError:
    H1V2 = None

try:
    from liquidctl.driver.smart_device import ControlHub
except ImportError:
    ControlHub = None

try:
    from liquidctl.driver.hydro_pro import HydroPro
except ImportError:
    HydroPro = None

from liquidctl_server.models import (
    BadRequestException,
    DeviceStatus,
    LiquidctlException,
    StatusValue,
)
from liquidctl_server.service.config import (
    DEVICE_OPERATION_TIMEOUT,
    DEVICE_STATUS_TIMEOUT,
    MAX_INIT_RETRIES,
)
from liquidctl_server.service.executor import DeviceExecutor

logger = logging.getLogger(__name__)


class LiquidctlService:
    """Service for managing liquidctl devices with thread-safe operations."""

    def __init__(self) -> None:
        self.devices: Dict[int, BaseDriver] = {}
        self.device_status_cache: Dict[int, List[StatusValue]] = {}
        self.speed_channels: Dict[int, List[str]] = {}
        self.previous_duty: Dict[str, Union[str, int, None]] = {}
        self._executor: DeviceExecutor = DeviceExecutor()

    def __enter__(self) -> "LiquidctlService":
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self.shutdown()
        if exc_value is not None:
            logger.error(
                f"Exception during service context: {exc_value}", exc_info=True
            )

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

            self.speed_channels[device_id] = self._get_speed_channels(lc_device)

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

            return self._build_device_status(device_id, lc_device, status_values)

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
                device_id,
                lambda dev_id=device_id: self._long_async_status_request(dev_id),
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

        return self._build_device_status(device_id, lc_device, status_values)

    def _build_status_from_cache(
        self, device_id: int, lc_device: BaseDriver
    ) -> Optional[DeviceStatus]:
        """Build a DeviceStatus from cached values."""
        cached = self.device_status_cache.get(device_id)
        if cached is None:
            return None

        return self._build_device_status(device_id, lc_device, cached)

    def _build_device_status(
        self, device_id: int, lc_device: BaseDriver, status_values: List[StatusValue]
    ) -> DeviceStatus:
        return DeviceStatus(
            id=device_id,
            description=lc_device.description,
            status=status_values,
            speed_channels=self.speed_channels.get(device_id, []),
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

    def log_device_details(self) -> None:
        """Dump everything useful about each device for RGB/debug troubleshooting."""
        introspect = (
            "_color_channels",
            "_color_modes",
            "_speed_channels",
            "_mled_count",
            "_led_count",
            "_fan_count",
            "_animation_speeds",
            "_mled",
        )
        logger.info("=== Device inventory (%d device(s)) ===", len(self.devices))
        for device_id, dev in self.devices.items():
            logger.info(
                "Device #%d: %s  [driver=%s, vid=%04x, pid=%04x]",
                device_id,
                dev.description,
                type(dev).__name__,
                getattr(dev, "vendor_id", 0) or 0,
                getattr(dev, "product_id", 0) or 0,
            )
            for attr in introspect:
                if hasattr(dev, attr):
                    try:
                        value = getattr(dev, attr)
                    except Exception as exc:  # pragma: no cover - defensive
                        value = f"<error: {exc}>"
                    logger.info("    %s = %r", attr, value)
        logger.info("=== End device inventory ===")

    def _resolve_device_id(self, device_match: str) -> Optional[int]:
        """Find a device id whose description contains device_match (case-insensitive)."""
        needle = device_match.lower()
        for device_id, lc_device in self.devices.items():
            if needle in lc_device.description.lower():
                return device_id
        return None

    def set_color(
        self,
        device_match: str,
        channel: str,
        mode: str,
        colors: List[Tuple[int, int, int]],
    ) -> None:
        """Set per-LED colors for a device channel via the serialized queue."""
        logger.info(
            "set_color RX: device_match=%r channel=%r mode=%r ncolors=%d first=%s last=%s",
            device_match,
            channel,
            mode,
            len(colors),
            colors[0] if colors else None,
            colors[-1] if colors else None,
        )

        device_id = self._resolve_device_id(device_match)
        if device_id is None:
            known = [d.description for d in self.devices.values()]
            logger.error(
                "set_color: no device matching %r (known: %s)", device_match, known
            )
            raise BadRequestException(f"No device matching '{device_match}'")

        lc_device = self.devices[device_id]
        logger.info(
            "set_color: resolved device #%d -> %s", device_id, lc_device.description
        )

        try:
            color_job = self._executor.submit(
                device_id,
                lc_device.set_color,
                channel=channel,
                mode=mode,
                colors=colors,
            )
            color_job.result(timeout=DEVICE_OPERATION_TIMEOUT)
            logger.info(
                "set_color: applied channel=%r mode=%r on device #%d",
                channel,
                mode,
                device_id,
            )

        except FuturesTimeoutError:
            logger.error(f"Timeout setting color for device #{device_id}")
        except Exception as e:
            logger.exception(
                "set_color FAILED on device #%d (channel=%r mode=%r ncolors=%d): %s",
                device_id,
                channel,
                mode,
                len(colors),
                e,
            )

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
        self.speed_channels.clear()
        self.previous_duty.clear()

    @staticmethod
    def _get_speed_channels(lc_device: BaseDriver) -> List[str]:
        """Controllable speed channels reported by the driver (no uniform API exists)."""

        def is_a(driver_cls) -> bool:
            return driver_cls is not None and isinstance(lc_device, driver_cls)

        if (
            isinstance(lc_device, (SmartDevice2, SmartDevice))
            or is_a(ControlHub)
            or is_a(H1V2)
        ):
            return list(getattr(lc_device, "_speed_channels", {}).keys())
        if is_a(Aquacomputer):
            return list(
                getattr(lc_device, "_device_info", {}).get("fan_ctrl", {}).keys()
            )
        if is_a(CommanderCore):
            return ["pump"] if getattr(lc_device, "_has_pump", False) else []
        if isinstance(lc_device, (CommanderPro, HydroPlatinum)):
            return list(getattr(lc_device, "_fan_names", []))
        if is_a(HydroPro):
            return [f"fan{i + 1}" for i in range(getattr(lc_device, "_fan_count", 0))]
        return []

    @staticmethod
    def _stringify_status(
        statuses: Union[List[Tuple[str, Union[str, int, float], str]], None],
    ) -> List[StatusValue]:
        """Convert raw liquidctl status to StatusValue list."""
        if statuses is None:
            return []

        result: List[StatusValue] = []
        for status in statuses:
            try:
                value = float(status[1])
            except (ValueError, TypeError):
                value = None

            result.append(
                StatusValue(key=str(status[0]), value=value, unit=str(status[2]))
            )

        return result
