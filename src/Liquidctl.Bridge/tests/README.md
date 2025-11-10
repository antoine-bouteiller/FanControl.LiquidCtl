# Liquidctl Bridge Tests

This directory contains unit tests for the Python liquidctl bridge.

## Test Framework

- **pytest**: Python testing framework
- **pytest-mock**: Mocking support for pytest
- **unittest.mock**: Python standard library mocking

## Test Coverage

### test_liquidctl_service.py
Tests for the liquidctl service that manages hardware devices:
- `_formatString`: Channel name formatting and normalization
  - Fan number formatting (e.g., "fan 1" → "fan1")
  - Duty suffix removal (e.g., "pump duty" → "pump")
  - Generic string lowercasing
- `LiquidctlService`: Device management
  - Initialization and context manager behavior
  - Device discovery and initialization
  - Status retrieval with proper formatting
  - Fixed speed setting with deduplication
  - Control mode filtering
  - Shutdown and cleanup
  - Error handling

### test_models.py
Tests for data models using msgspec:
- `StatusValue`: Sensor value representation
- `SpeedKwargs`: Speed control parameters
- `FixedSpeedRequest`: Speed setting request structure
- `PipeRequest`: Pipe communication request
- `DeviceStatus`: Device information and status
- `Mode`: Pipe communication mode enum
- Custom exceptions: `PipeError`, `LiquidctlException`, `BadRequestException`
- JSON serialization/deserialization

### test_server.py
Tests for the server and command processing:
- `setup_logging`: Logging configuration with level parsing
- `process_command`: Command routing and handling
  - get.statuses command
  - set.fixed_speed command with validation
  - Unknown command handling

### test_client.py (Existing)
Manual integration test client for named pipe communication.
**Note**: This is not a pytest test file but a utility for manual testing.

### test.py (Existing)
Manual integration test script.
**Note**: This is not a pytest test file but a utility for manual testing.

## Running Tests

### All Tests
```bash
cd src/Liquidctl.Bridge
poetry run pytest tests/ -v
```

### Specific Test File
```bash
poetry run pytest tests/test_liquidctl_service.py -v
```

### With Coverage
```bash
poetry run pytest tests/ --cov=liquidctl_bridge --cov-report=html
```

### Watch Mode
```bash
poetry run pytest tests/ --watch
```

## CI/CD Integration

Tests are automatically run in the GitHub Actions workflow (`.github/workflows/build-check.yaml`) on every pull request.

## Test Exclusions

The following files are excluded from pytest collection:
- `test_client.py`: Manual test utility (not a pytest test)
- `test.py`: Manual integration test script

To exclude them from pytest, they should not contain test functions with the `test_` prefix or be explicitly excluded in pytest configuration.

## Notes

### Mocking liquidctl
Tests mock the `liquidctl.find_liquidctl_devices()` function and device drivers since:
- Tests run in CI/CD without actual hardware
- Liquidctl requires specific USB/HID devices to be connected
- Mocking allows testing business logic independently

### Windows-Specific Code
The `pipe_server.py` module uses Windows-specific APIs (ctypes with kernel32). Tests for this module are conceptual because:
- They require Windows OS to run
- Named pipe testing requires actual pipe infrastructure
- Integration tests would be more appropriate than unit tests

### Integration Testing
Full integration tests would require:
1. Running the actual server process
2. Creating a test client connection
3. Mocking liquidctl devices
4. Testing the full request/response cycle

Consider adding these as separate integration test suites.
