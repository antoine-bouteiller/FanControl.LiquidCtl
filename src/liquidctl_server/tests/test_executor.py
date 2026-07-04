import queue
import threading
from concurrent.futures import Future

import pytest

from liquidctl_server.service.executor import DeviceExecutor, _DeviceJob, _queue_worker


class TestDeviceJobRun:
    def test_success_sets_result(self):
        future = Future()
        job = _DeviceJob(future, lambda x: x * 2, x=5)
        job.run()
        assert future.result() == 10

    def test_exception_sets_on_future_without_reraising(self):
        future = Future()
        job = _DeviceJob(future, lambda: (_ for _ in ()).throw(ValueError("boom")))
        job.run()
        with pytest.raises(ValueError, match="boom"):
            future.result()

    def test_cancelled_future_fn_not_called(self):
        future = Future()
        future.cancel()
        called = []
        job = _DeviceJob(future, lambda: called.append(True))
        job.run()
        assert called == []


class TestDeviceQueueEmpty:
    def test_unknown_device_returns_true(self):
        executor = DeviceExecutor()
        assert executor.device_queue_empty(999) is True


class TestSetNumberOfDevices:
    def test_creates_queue_per_device(self):
        executor = DeviceExecutor()
        executor.set_number_of_devices(3)
        try:
            assert executor.device_queue_empty(1) is True
            assert executor.device_queue_empty(2) is True
            assert executor.device_queue_empty(3) is True
        finally:
            executor.shutdown()

    def test_zero_devices_creates_no_queues(self):
        executor = DeviceExecutor()
        executor.set_number_of_devices(0)
        assert executor.device_queue_empty(1) is True


class TestSubmitAndShutdown:
    def test_submit_runs_job_and_returns_result(self):
        executor = DeviceExecutor()
        executor.set_number_of_devices(1)
        try:
            future = executor.submit(1, lambda x: x + 1, x=41)
            assert future.result(timeout=2.0) == 42
        finally:
            executor.shutdown()

    def test_shutdown_drains_and_joins(self):
        executor = DeviceExecutor()
        executor.set_number_of_devices(2)
        executor.submit(1, lambda: None)
        executor.submit(2, lambda: None)

        executor.shutdown()

        assert executor.device_queue_empty(1) is True


class TestQueueWorker:
    def test_none_sentinel_terminates_worker(self):
        q = queue.SimpleQueue()
        thread = threading.Thread(target=_queue_worker, args=(q,))
        thread.start()
        q.put(None)
        thread.join(timeout=1.0)
        assert not thread.is_alive()

    def test_worker_survives_failing_job(self):
        q = queue.SimpleQueue()
        failing, ok = Future(), Future()
        q.put(_DeviceJob(failing, lambda: (_ for _ in ()).throw(ValueError("boom"))))
        q.put(_DeviceJob(ok, lambda: 99))
        q.put(None)

        thread = threading.Thread(target=_queue_worker, args=(q,))
        thread.start()
        thread.join(timeout=2.0)

        assert not thread.is_alive()
        with pytest.raises(ValueError, match="boom"):
            failing.result()
        assert ok.result() == 99

    def test_jobs_processed_before_sentinel(self):
        q = queue.SimpleQueue()
        results = []
        future1, future2 = Future(), Future()
        q.put(_DeviceJob(future1, lambda: results.append(1) or 1))
        q.put(_DeviceJob(future2, lambda: results.append(2) or 2))
        q.put(None)

        thread = threading.Thread(target=_queue_worker, args=(q,))
        thread.start()
        thread.join(timeout=2.0)

        assert results == [1, 2]
        assert future1.result() == 1
        assert future2.result() == 2
