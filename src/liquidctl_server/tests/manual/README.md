# Manual / Hardware Integration Tests

These files require a running `liquidctl_server` bridge connected to real hardware via Windows named
pipes. They cannot be run in CI.

## Prerequisites

1. A Windows machine with compatible AIO or fan controller hardware
2. Start the bridge in test mode: `uv run python -m liquidctl_server --test`
3. Run the integration test: `uv run python -m tests.manual.test`

## Files

- `test_client.py` — Named-pipe client helper (Win32 / ctypes). Used by `test.py`.
- `test.py` — Sends `get.statuses` and `set.fixed_speed` commands to real hardware.
