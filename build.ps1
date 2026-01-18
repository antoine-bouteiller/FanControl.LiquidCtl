$ErrorActionPreference = "Stop"

Set-Location .\src\FanControl.Liquidctl
dotnet build -c Release
if ($LASTEXITCODE -ne 0)
{
    Set-Location ..\..
    Write-Host "dotnet build failed! Check the output above for errors." -ForegroundColor Red
    exit 1
}

Set-Location ..\liquidctl_server
uv sync
uv run python -m nuitka `
    --standalone `
    --onefile `
    --output-filename=liquidctl_server.exe `
    --output-dir=dist `
    --remove-output `
    --assume-yes-for-downloads `
    --windows-console-mode=disable `
    --mingw64 `
    .\liquidctl_server\server.py

if ($LASTEXITCODE -ne 0)
{
    Set-Location ..\..
    Write-Host "Nuitka build failed! Check the output above for errors." -ForegroundColor Red
    exit 1
}

# Create output folder
Set-Location ..\..
Remove-Item -Recurse -Force FanControl.LiquidCtl -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path FanControl.LiquidCtl | Out-Null

# Copy artifacts
Copy-Item .\src\FanControl.Liquidctl\bin\Release\net8.0\FanControl.Liquidctl.dll .\FanControl.LiquidCtl\
Copy-Item .\src\liquidctl_server\dist\liquidctl_server.exe .\FanControl.LiquidCtl\

Write-Host "Build complete! Output in FanControl.LiquidCtl\" -ForegroundColor Green
