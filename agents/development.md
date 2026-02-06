# Development Guidelines

## Error Handling

1. **Always catch exceptions in `Update()`** - Prevents FanControl crashes
2. **Use `RefreshRequested` for critical errors** - Allows recovery
3. **Log errors with `IPluginLogger`** - Helps debugging
4. **Graceful degradation** - Continue with available devices

```csharp
public void Update()
{
    try {
        var devices = liquidctl.GetStatuses();
        // Update sensors
    }
    catch (IOException ex) {
        logger.Log($"Bridge communication error: {ex.Message}");
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
```

## Performance

- `Update()` called every ~1 second - keep it lightweight
- Cache sensor objects - don't recreate each update
- Batch device status requests
- Use timeouts for bridge communication (default: 5 seconds)
- Avoid blocking operations

## Thread Safety

- FanControl may call methods from different threads
- Use thread-safe collections (`ConcurrentDictionary`) for sensor storage
- Protect bridge communication with locks if needed
- Ensure `RefreshRequested` event is thread-safe

## Building

**C# Plugin:**
```bash
cd src/FanControl.Liquidctl
dotnet build
```

**Python Bridge:**
```bash
cd src/Liquidctl
uv sync
uv build
```

**Complete Release:**
```powershell
.\build.ps1
```

## Testing

**Manual Testing:**
1. Copy DLL to FanControl plugins folder
2. Launch FanControl
3. Check logs for initialization
4. Verify sensors appear in UI

**Python Bridge Testing:**
```bash
cd src/Liquidctl
uv run pytest tests/
```

## Version Information

- .NET Version: 8.0
- C# Language Version: Latest
- Python Version: 3.11+
- FanControl Plugin Interface: IPlugin3
- Target Platform: Windows (win-x64)
