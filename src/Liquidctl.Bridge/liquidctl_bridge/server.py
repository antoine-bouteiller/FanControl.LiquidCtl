import argparse
import logging
import sys
import time
from typing import Any, Callable, Dict, Optional

import msgspec

from liquidctl_bridge.liquidctl_service import LiquidctlService
from liquidctl_bridge.models import (
    BadRequestException,
    BridgeResponse,
    FixedSpeedRequest,
    MessageStatus,
    PipeRequest,
)
from liquidctl_bridge.pipe_server import Server

logger = logging.getLogger(__name__)

def handle_get_statuses(service: LiquidctlService, data: Any) -> Any:
    return service.get_statuses()

def handle_set_fixed_speed(service: LiquidctlService, data: Optional[FixedSpeedRequest]) -> Any:
    if data is None:
        raise BadRequestException("Missing data for set.fixed_speed")

    return service.set_fixed_speed(
        data.device_id,
        msgspec.to_builtins(data.speed_kwargs)
    )

COMMAND_HANDLERS: Dict[str, Callable] = {
    "get.statuses": handle_get_statuses,
    "set.fixed_speed": handle_set_fixed_speed,
}

def setup_logging(log_level: str = "INFO") -> None:
    logging.basicConfig(
        level=getattr(logging, log_level.upper(), logging.INFO),
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
        handlers=[logging.StreamHandler(sys.stdout)],
    )

def process_request(raw_msg: bytes, service: LiquidctlService) -> bytes:
    """Decodes binary MsgPack, runs logic, and returns binary MsgPack."""
    try:
        request = msgspec.msgpack.decode(raw_msg, type=PipeRequest)

        handler = COMMAND_HANDLERS.get(request.command)
        if not handler:
            raise BadRequestException(f"Unknown command: {request.command}")

        result = handler(service, request.data)
        response = BridgeResponse(status=MessageStatus.SUCCESS, data=result)

    except (msgspec.DecodeError, msgspec.ValidationError) as e:
        logger.warning(f"Invalid MessagePack received: {e}")
        response = BridgeResponse(status=MessageStatus.ERROR, error=f"Protocol Error: {e}")

    except BadRequestException as e:
        logger.warning(f"Bad Request: {e}")
        response = BridgeResponse(status=MessageStatus.ERROR, error=str(e))

    except Exception as e:
        logger.exception("Internal Error processing command")
        response = BridgeResponse(status=MessageStatus.ERROR, error=f"Internal Error: {e}")

    return msgspec.msgpack.encode(response)

def run_server_loop(service: LiquidctlService, pipe: Server) -> None:
    while True:
        raw_msg = pipe.read()

        if raw_msg:
            response_bytes = process_request(raw_msg, service)
            pipe.write(response_bytes)
        else:
            time.sleep(0.05)

def main():
    parser = argparse.ArgumentParser(description="Liquidctl Bridge Server")
    parser.add_argument("--log-level", default="INFO", help="Logging level")
    args = parser.parse_args()

    setup_logging(args.log_level)
    pipe_name = "LiquidCtlPipe"

    try:
        with LiquidctlService() as service, Server(name=pipe_name) as pipe:
            logger.info("Initializing Liquidctl devices...")
            service.initialize_all()
            logger.info(f"Bridge Server listening on \\\\.\\pipe\\{pipe_name}")

            run_server_loop(service, pipe)

    except KeyboardInterrupt:
        logger.info("Stopping server...")
    except Exception as e:
        logger.critical(f"Fatal crash: {e}", exc_info=True)
        sys.exit(1)

if __name__ == "__main__":
    main()
