import logging
import sys

from liquidctl_bridge.models import FixedSpeedRequest, MessageStatus, SpeedKwargs
from tests.test_client import TestClient

pipe_name = "LiquidCtlPipe"


logging.basicConfig(
    level=logging.DEBUG,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)

logger = logging.getLogger(__name__)


def main():
    with TestClient(pipe_name) as client:

        res = client.sendRequest("get.statuses")
        logger.info(res)
        if res is not None and res.status == MessageStatus.SUCCESS:
            devices = res.data
            logger.info(devices)
            for device in devices:
                logger.info(device)
                status = next(status for status in device.status if status.unit == "%")
                client.sendRequest(
                    "set.fixed_speed",
                    FixedSpeedRequest(
                        device_id=device.id,
                        speed_kwargs=SpeedKwargs(channel=status.key, duty=0),
                    ),
                )
        else:
            raise Exception(f"Request failed: {res.error if res else 'No response'}")


if __name__ == "__main__":
    main()
