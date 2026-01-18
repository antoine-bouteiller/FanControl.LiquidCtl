
$ErrorActionPreference = "Stop"
Set-Location .\src\FanControl.Liquidctl
dotnet build
if ($LASTEXITCODE -ne 0)
{
    Set-Location ..\..
    Write-Host "dotnet build failed! Check the output above for errors." -ForegroundColor Red
    exit 1
}

Set-Location ..\Liquidctl
uv run pyinstaller --onefile ".\liquidctl_server\server.py" -n liquidctl --clean
if ($LASTEXITCODE -ne 0)
{
    Set-Location ..\..
    Write-Host "pyinstaller failed! Check the output above for errors." -ForegroundColor Red
    exit 1
}

Set-Location ..\..
Remove-Item FanControl.liquidCtl -ErrorAction SilentlyContinue
mkdir FanControl.liquidCtl

Move-Item .\src\FanControl.Liquidctl\bin\Debug\net8.0\FanControl.Liquidctl.dll .\FanControl.liquidCtl\FanControl.Liquidctl.dll
Move-Item .\src\Liquidctl\dist\liquidctl.exe .\LiquidCtl\liquidctl_server.exe
