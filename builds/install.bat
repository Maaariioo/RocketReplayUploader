@echo off
setlocal
set APP_NAME=Rocket Replay Uploader
set SRC=%~dp0rocket-replay-uploader.exe
set DEST=%LOCALAPPDATA%\RocketReplayUploader\rocket-replay-uploader.exe

if not exist "%SRC%" (
  echo rocket-replay-uploader.exe not found next to install.bat
  pause
  exit /b 1
)

mkdir "%LOCALAPPDATA%\RocketReplayUploader" 2>nul
copy /y "%SRC%" "%DEST%"

echo Installed to %DEST%
set /p AUTOSTART="Start automatically at login? (y/N): "
if /i "%AUTOSTART%"=="y" (
  reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v RocketReplayUploader /d "\"%DEST%\"" /f
  echo Autostart enabled.
)

echo Done. You can run it from: %DEST%
pause
