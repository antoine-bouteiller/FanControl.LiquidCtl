from typing import List
from tests.test_client import TestClient
from liquidctl_bridge.models import DeviceStatus, FixedSpeedRequest, SpeedKwargs
import msgspec
import logging
import sys

pipe_name = "LiquidCtlPipe"


logging.basicConfig(
    level=logging.DEBUG,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)

logger = logging.getLogger(__name__)


def main():
    with TestClient(pipe_name) as client:
        client.sendRequest("initialize")

        res = client.sendRequest("get.statuses")
        if res is not None:
            res = msgspec.json.decode(res, type=List[DeviceStatus])
            for device in res:
                logger.info(device)
                status = next(status for status in device.status if status.unit == "%")
                client.sendRequest(
                    "set.fixed_speed",
                    FixedSpeedRequest(
                        device_id=device.id,
                        speed_kwargs=SpeedKwargs(
                            channel=status.key.replace(" ", ""), duty=10
                        ),
                    ),
                )
        else:
            raise Exception("No response")


if __name__ == "__main__":
    main()
