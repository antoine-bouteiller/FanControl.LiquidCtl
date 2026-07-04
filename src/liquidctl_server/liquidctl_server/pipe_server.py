import logging
import threading
from typing import Callable, Optional

from liquidctl_server import win32_pipe
from liquidctl_server.models import PipeError

logger = logging.getLogger(__name__)


class PipeServer:
    def __init__(self, name: str, handler: Callable[[bytes], bytes]) -> None:
        self.name = name
        self.pipe_path = win32_pipe.pipe_path(name)
        self._handler = handler
        self._shutdown = threading.Event()
        self._handle: Optional[int] = None
        self._handle_lock = threading.Lock()
        self._thread = threading.Thread(target=self._serve_forever, daemon=True)

    def start(self) -> None:
        logger.info(f"Starting Named Pipe server thread for: {self.name}")
        self._thread.start()

    def stop(self) -> None:
        self._shutdown.set()
        self._close_handle()
        if self._thread.is_alive():
            self._thread.join(timeout=2.0)

    def _close_handle(self) -> None:
        with self._handle_lock:
            if self._handle is not None:
                win32_pipe.close(self._handle)
                self._handle = None

    def _serve_forever(self) -> None:
        while not self._shutdown.is_set():
            try:
                handle = win32_pipe.create_server_pipe(self.name)
            except PipeError as err:
                logger.error(f"Could not create pipe {self.name}: {err}")
                self._shutdown.wait(2.0)
                continue

            with self._handle_lock:
                if self._shutdown.is_set():
                    win32_pipe.close(handle)
                    return
                self._handle = handle

            try:
                win32_pipe.wait_for_client(handle)
                logger.info("Client connected")
                self._serve_client(handle)
            except PipeError as err:
                if not self._shutdown.is_set():
                    logger.info(f"Client session ended: {err}")
            finally:
                self._close_handle()

    def _serve_client(self, handle: int) -> None:
        while not self._shutdown.is_set():
            request = win32_pipe.read_message(handle)
            win32_pipe.write_message(handle, self._handler(request))
