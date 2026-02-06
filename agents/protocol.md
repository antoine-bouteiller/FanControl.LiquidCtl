# Communication Protocol

## Named Pipes

- **Pipe Name:** `liquidctl_<random>`
- **Message Format:** MessagePack binary

## Commands

> Note: Examples show logical structure. Actual wire format is MessagePack binary.

### Initialize
```json
{
  "command": "initialize"
}
```

### Get Status
```json
{
  "command": "get_status"
}
```

**Response:**
```json
[
  {
    "description": "NZXT Kraken X63",
    "status": [
      {
        "key": "Liquid temperature",
        "value": 28.5,
        "unit": "°C"
      },
      {
        "key": "Pump speed",
        "value": 2500,
        "unit": "rpm"
      },
      {
        "key": "pump",
        "value": 75,
        "unit": "%"
      }
    ]
  }
]
```

### Set Speed
```json
{
  "command": "set_speed",
  "device": "NZXT Kraken X63",
  "channel": "pump",
  "duty": 80
}
```

## Error Responses

```json
{
  "error": "Device not found",
  "code": "DEVICE_NOT_FOUND"
}
```
