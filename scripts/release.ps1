$ErrorActionPreference = "Stop"

Push-Location src/FanControl.Liquidctl
dotnet build -c Release
Pop-Location

Write-Host "Building Python Bridge..."
Push-Location src/liquidctl_server
uv sync
uv run python -m nuitka `
    --standalone `
    --output-dir=dist/standalone `
    --output-filename=liquidctl_server.exe `
    --assume-yes-for-downloads `
    --windows-console-mode=disable `
    --windows-icon-from-ico=liquidctl.ico `
    .\liquidctl_server\server.py
Pop-Location

$dll = "src/FanControl.Liquidctl/bin/Release/net10.0/FanControl.Liquidctl.dll"
$pythonDist = "src/liquidctl_server/dist/standalone/server.dist"
$pythonTarget = "src/liquidctl_server/dist/standalone/liquidctl_server"

if (Test-Path $pythonTarget) {
    Remove-Item -Recurse -Force $pythonTarget
}

Rename-Item -Path $pythonDist -NewName "liquidctl_server"

if (Test-Path "FanControl.Liquidctl.zip") {
    Remove-Item "FanControl.Liquidctl.zip"
}

Compress-Archive -Path $dll, $pythonTarget -DestinationPath "FanControl.Liquidctl.zip"
