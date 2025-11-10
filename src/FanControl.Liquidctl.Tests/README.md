# FanControl.Liquidctl Tests

This directory contains unit tests for the FanControl.Liquidctl plugin.

## Test Framework

- **xUnit**: Modern .NET testing framework
- **Moq**: Mocking framework for interfaces
- **FluentAssertions**: Readable assertions

## Test Coverage

### LiquidctlDeviceTests.cs
Tests for device sensor models:
- `DeviceSensor`: Read-only sensor implementation
  - ID generation (spaces removed from device description and channel key)
  - Name formatting
  - Value retrieval
  - Update functionality
- `ControlSensor`: Control sensor implementation (writable)
  - Initial value storage
  - Set operation with bridge wrapper integration
  - Duty value rounding
  - Reset to initial value
  - Paired fan sensor ID management

### LiquidctlBridgeWrapperTests.cs
Tests for the bridge wrapper that communicates with Python bridge:
- Constructor and disposal
- Shutdown behavior
- Error handling for failed connections
- Request/response serialization (PipeRequest, FixedSpeedRequest)
- DeviceStatus deserialization

### LiquidctlPluginTests.cs
Tests for the main plugin implementation:
- Plugin name and metadata
- Lifecycle methods (Initialize, Load, Update, Close)
- Dispose pattern
- Null parameter validation

## Running Tests

### Command Line
```bash
cd src/FanControl.Liquidctl.Tests
dotnet test --configuration Debug
```

### With Coverage
```bash
dotnet test --configuration Debug --collect:"XPlat Code Coverage"
```

### Visual Studio
Open the solution and use Test Explorer to run tests.

## CI/CD Integration

Tests are automatically run in the GitHub Actions workflow (`.github/workflows/build-check.yaml`) on every pull request.

## Notes

Some tests are conceptual (marked with comments) because they would require:
- Dependency injection of the bridge wrapper for proper mocking
- Integration test infrastructure for named pipe communication
- Actual hardware or hardware simulators for end-to-end testing

These represent areas where the codebase could be refactored to improve testability.
