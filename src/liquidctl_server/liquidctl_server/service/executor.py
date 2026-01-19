import queue
import sys
from concurrent.futures import Future, ThreadPoolExecutor
from typing import Any, Callable, Dict, Optional


class _DeviceJob:
    """A job to be executed on a specific device."""

    def __init__(self, future: Future, fn: Callable, **kwargs: Any) -> None:
        self.future = future
        self.fn = fn
        self.kwargs = kwargs

    def run(self) -> None:
        """Execute the job and set the result on the future."""
        if not self.future.set_running_or_notify_cancel():
            return
        try:
            result = self.fn(**self.kwargs)
        except BaseException as exc:
            self.future.set_exception(exc)
        else:
            self.future.set_result(result)


def _queue_worker(dev_queue: queue.SimpleQueue) -> None:
    """Worker that processes jobs from a device queue sequentially."""
    try:
        while True:
            device_job: Optional[_DeviceJob] = dev_queue.get()
            if device_job is None:
                return  # Shutdown signal
            device_job.run()
            del device_job
    except BaseException as exc:
        sys.stderr.write(f"Exception in device worker: {exc}\n")


class DeviceExecutor:
    """
    Executor that maintains per-device job queues.

    Simultaneous communications per device result in mangled USB/HID data,
    so each device has its own job queue processed by a dedicated worker thread.
    This enables parallel communication with multiple devices while keeping
    per-device communication synchronous.
    """

    def __init__(self) -> None:
        self._device_queues: Dict[int, queue.SimpleQueue] = {}
        self._thread_pool: Optional[ThreadPoolExecutor] = None

    def set_number_of_devices(self, number_of_devices: int) -> None:
        """Initialize queues and workers for the given number of devices."""
        if number_of_devices < 1:
            return

        self._thread_pool = ThreadPoolExecutor(max_workers=number_of_devices)
        for dev_id in range(1, number_of_devices + 1):
            dev_queue: queue.SimpleQueue = queue.SimpleQueue()
            self._device_queues[dev_id] = dev_queue
            self._thread_pool.submit(_queue_worker, dev_queue)

    def submit(self, device_id: int, fn: Callable, **kwargs: Any) -> Future:
        """Submit a job to the device's queue and return a Future."""
        future: Future = Future()
        device_job = _DeviceJob(future, fn, **kwargs)
        self._device_queues[device_id].put(device_job)
        return future

    def device_queue_empty(self, device_id: int) -> bool:
        """Check if a device's job queue is empty."""
        dev_queue = self._device_queues.get(device_id)
        return dev_queue.empty() if dev_queue else True

    def shutdown(self) -> None:
        """Shutdown all workers and clear queues."""
        for dev_queue in self._device_queues.values():
            dev_queue.put(None)  # Signal workers to stop

        if self._thread_pool is not None:
            self._thread_pool.shutdown(wait=True)
            self._thread_pool = None

        self._device_queues.clear()
