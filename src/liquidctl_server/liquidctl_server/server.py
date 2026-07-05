import argparse
import logging
import os
import sys
import time
from typing import Any, Callable, Dict, List

import msgspec

from liquidctl_server.models import (
    BadRequestException,
    BridgeResponse,
    FixedSpeedRequest,
    LedRequest,
    MessageStatus,
    PipeRequest,
)
from liquidctl_server.pipe_server import PipeServer
from liquidctl_server.service import LiquidctlService

logger = logging.getLogger(__name__)


def _decode_data(data: msgspec.Raw, type_, command: str):
    if data is None or bytes(data) == b"null":
        raise BadRequestException(f"Missing data for {command}")
    return msgspec.json.decode(data, type=type_)


def handle_get_statuses(service: LiquidctlService, data: msgspec.Raw) -> Any:
    return service.get_statuses()


def handle_set_fixed_speed(service: LiquidctlService, data: msgspec.Raw) -> Any:
    request = _decode_data(data, FixedSpeedRequest, "set.fixed_speed")
    speed_kwargs = {
        "channel": request.speed_kwargs.channel,
        "duty": request.speed_kwargs.duty,
    }
    return service.set_fixed_speed(request.device_id, speed_kwargs)


def _validate_colors(colors: List[List[int]]) -> None:
    for color in colors:
        if len(color) != 3 or not all(
            isinstance(c, int) and 0 <= c <= 255 for c in color
        ):
            raise BadRequestException(
                f"Invalid color {color!r}: expected 3 ints in [0, 255]"
            )


def handle_set_led(service: LiquidctlService, data: msgspec.Raw) -> Any:
    request = _decode_data(data, LedRequest, "set.led")
    _validate_colors(request.colors)
    colors = [tuple(color) for color in request.colors]
    return service.set_color(request.device, request.channel, request.mode, colors)


COMMAND_HANDLERS: Dict[str, Callable] = {
    "get.statuses": handle_get_statuses,
    "set.fixed_speed": handle_set_fixed_speed,
    "set.led": handle_set_led,
}


def setup_logging(log_level: str = "INFO") -> None:
    logging.basicConfig(
        level=getattr(logging, log_level.upper(), logging.INFO),
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
        handlers=[logging.StreamHandler(sys.stdout)],
    )


def process_request(raw_msg: bytes, service: LiquidctlService) -> bytes:
    """Decodes JSON, runs logic, and returns JSON."""
    try:
        request = msgspec.json.decode(raw_msg, type=PipeRequest)

        handler = COMMAND_HANDLERS.get(request.command)
        if not handler:
            raise BadRequestException(f"Unknown command: {request.command}")

        result = handler(service, request.data)
        response = BridgeResponse(status=MessageStatus.SUCCESS, data=result)

    except (msgspec.DecodeError, msgspec.ValidationError) as e:
        logger.warning(f"Invalid JSON received: {e}")
        response = BridgeResponse(
            status=MessageStatus.ERROR, error=f"Protocol Error: {e}"
        )

    except BadRequestException as e:
        logger.warning(f"Bad Request: {e}")
        response = BridgeResponse(status=MessageStatus.ERROR, error=str(e))

    except Exception as e:
        logger.exception("Internal Error processing command")
        response = BridgeResponse(
            status=MessageStatus.ERROR, error=f"Internal Error: {e}"
        )

    return msgspec.json.encode(response)


def main():
    parser = argparse.ArgumentParser(description="Liquidctl Bridge Server")
    parser.add_argument("--log-level", default="INFO", help="Logging level")
    parser.add_argument(
        "--test", default=False, action="store_true", help="Running as test"
    )
    args = parser.parse_args()

    # Env override so the bridge can be made verbose without changing the spawn
    # args of the host plugin (set LIQUIDCTL_BRIDGE_LOG=DEBUG before launch).
    log_level = os.environ.get("LIQUIDCTL_BRIDGE_LOG") or args.log_level
    setup_logging(log_level)
    suffix = "Test" if args.test else ""
    pipe_name = f"LiquidCtlPipe{suffix}"
    # Dedicated pipe for the RGB plugin: the fan client holds its connection open
    # persistently and the pipe server is single-client per instance, so RGB needs
    # its own pipe. Both feed the same service → same per-device DeviceExecutor
    # queue, so the single-owner-per-HID guarantee is preserved.
    rgb_pipe_name = f"LiquidCtlPipe{suffix}Rgb"

    try:
        with LiquidctlService() as service:
            logger.info("Initializing Liquidctl devices...")
            service.initialize_all()
            service.log_device_details()

            def handle(raw_msg: bytes) -> bytes:
                return process_request(raw_msg, service)

            servers = [
                PipeServer(pipe_name, handle),
                PipeServer(rgb_pipe_name, handle),
            ]
            for server in servers:
                server.start()
            logger.info(f"Bridge Server listening on \\\\.\\pipe\\{pipe_name}")
            logger.info(f"RGB Bridge Server listening on \\\\.\\pipe\\{rgb_pipe_name}")

            try:
                while all(server.is_alive() for server in servers):
                    time.sleep(1)
                logger.critical("A pipe server thread died; exiting for restart")
                sys.exit(1)
            except KeyboardInterrupt:
                logger.info("Stopping server...")
            finally:
                for server in servers:
                    server.stop()

    except KeyboardInterrupt:
        logger.info("Stopping server...")
    except Exception as e:
        logger.critical(f"Fatal crash: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    main()
