# Troubleshooting

## Bridge Not Starting

- Check Python executable is bundled correctly
- Verify Named Pipe permissions
- Check Windows Defender isn't blocking

## Device Not Detected

- Verify device is liquidctl-compatible
- Check USB connection and drivers
- Run liquidctl CLI directly to test:
  ```bash
  liquidctl list
  liquidctl status
  ```

## Communication Timeout

- Default timeout is 5 seconds
- Increase if devices are slow to respond
- Check for USB issues or device errors
