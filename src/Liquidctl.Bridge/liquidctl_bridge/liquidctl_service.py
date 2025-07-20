import logging
from typing import Dict, List, Tuple, Union

import liquidctl
from liquidctl.driver.base import BaseDriver
from liquidctl_bridge.models import (
    BadRequestException,
    DeviceStatus,
    LiquidctlException,
    StatusValue,
)

logger = logging.getLogger(__name__)


class LiquidctlService:
    def __init__(self) -> None:
        self.devices: Dict[int, BaseDriver] = {}

    def __enter__(self) -> "LiquidctlService":
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self.shutdown()

    def initialize_all(self) -> None:
        try:
            logger.debug("liquidctl.find_liquidctl_devices()")

            found_devices: List[BaseDriver] = list(liquidctl.find_liquidctl_devices())

            for index, lc_device in enumerate(found_devices):
                index_id = index + 1
                self.devices[index_id] = lc_device
                lc_device.connect()
            device_names = [device.description for device in found_devices]
            logger.info(f"Devices found: {device_names}")
        except ValueError as ve:  # ValueError can happen when no devices were found
            logger.debug("ValueError when trying to find all devices", exc_info=ve)
            logger.info("No Liquidctl devices detected")
        except Exception as e:
            logger.info("Error during initialisation", exc_info=e)

    def get_statuses(self) -> List[DeviceStatus]:
        logger.info(f"Getting devices statuses for {len(self.devices)} devices")
        if self.devices:
            devices = [
                DeviceStatus(
                    id=key,
                    description=lc_device.description,
                    bus=lc_device.bus,
                    address=lc_device.address,
                    status=self._stringify_status(lc_device.get_status()),
                )
                for key, lc_device in self.devices.items()
            ]
            return devices
        else:
            raise LiquidctlException("You must initialize the devices first")

    def set_fixed_speed(
        self, device_id: int, speed_kwargs: Dict[str, Union[str, int]]
    ) -> None:
        if self.devices.get(device_id) is None:
            raise BadRequestException(f"Device with id:{device_id} not found")
        logger.debug(
            f"Setting fixes speed for device: {device_id} with args: {speed_kwargs}"
        )
        try:
            lc_device = self.devices[device_id]
            logger.debug(
                f"LC #{device_id} {lc_device.__class__.__name__}.set_fixed_speed({speed_kwargs}) "
            )
            lc_device.set_fixed_speed(**speed_kwargs)
        except BaseException as err:
            logger.error("Error setting fixed speed:", exc_info=err)

    def shutdown(self) -> None:
        for device_id, lc_device in self.devices.items():
            logger.debug(
                f"LC #{device_id} {lc_device.__class__.__name__}.disconnect() "
            )
            lc_device.disconnect()
        self.devices.clear()

    @staticmethod
    def _stringify_status(
        statuses: Union[List[Tuple[str, Union[str, int, float], str]], None],
    ) -> List[StatusValue]:
        return (
            [
                StatusValue(
                    key=str(status[0].replace(" duty", "").lower()),
                    value=float(status[1]),
                    unit=str(status[2]),
                )
                for status in statuses
                if "control mode" not in status[0]
            ]
            if statuses is not None
            else []
        )
