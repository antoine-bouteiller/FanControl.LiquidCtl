
Set-Location .\src\FanControl.Liquidctl
dotnet build

Set-Location ..\Liquidctl.Bridge
poetry run pyinstaller --onefile ".\liquidctl_bridge\server.py" -n liquidctl_bridge --clean

Set-Location ..\..
Remove-Item FanControl.liquidCtl -ErrorAction SilentlyContinue
mkdir FanControl.liquidCtl

# $compress = @{
#   Path            = ".\bin\Release\FanControl.Liquidctl.dll", ".\liquidctl.exe", ".\liquidctl-license.txt"
#   DestinationPath = ".\FanControl.Liquidctl.zip"
# }
# Compress-Archive @compress

Move-Item .\src\FanControl.Liquidctl\bin\Debug\net8.0\FanControl.Liquidctl.dll .\FanControl.liquidCtl\FanControl.Liquidctl.dll
Move-Item .\src\Liquidctl.Bridge\dist\liquidctl_bridge.exe .\FanControl.liquidCtl\liquidctl_bridge.exe