import logging
import sys

import msgspec

from liquidctl_bridge.liquidctl_service import LiquidctlService
from liquidctl_bridge.models import BadRequestException, PipeRequest
from liquidctl_bridge.pipe_server import Server
import time

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[
        logging.FileHandler("liquidctl_bridge.log"),
        logging.StreamHandler(sys.stdout),
    ],
)
logger = logging.getLogger(__name__)

pipe_name = "LiquidCtlPipe"


def process_command(request: PipeRequest, liquidctl_service: LiquidctlService):
    match request.command:
        case "get.statuses":
            return liquidctl_service.get_statuses()
        case "set.fixed_speed":
            if request.data is None:
                raise BadRequestException("No data provided")
            return liquidctl_service.set_fixed_speed(
                request.data.device_id,
                msgspec.to_builtins(request.data.speed_kwargs),
            )


def handle_pipe_message(liquidctl_service: LiquidctlService, pipe: Server):
    rawmsg = pipe.read()
    if not rawmsg:
        return

    request = msgspec.json.decode(rawmsg, type=PipeRequest)
    response = process_command(request, liquidctl_service)
    pipe.write(msgspec.json.encode(response))


def main():
    with LiquidctlService() as liquidctl_service, Server(name=pipe_name) as pipe:
        logger.info("Started Liquidctl Bridge Server")
        liquidctl_service.initialize_all()
        while True:
            if pipe.alive:
                handle_pipe_message(liquidctl_service, pipe)
            else:
                time.sleep(0.2)


if __name__ == "__main__":
    main()
