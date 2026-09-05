Rocket Replay Uploader - builds

Files:
  rocket-replay-uploader.exe        single-file portable exe (just double-click, no install)
  rocket-replay-uploader.zip        same portable exe inside a normal zip
  rocket-replay-uploader-dlls.zip   unpacked build (exe + dlls)
  rocket-replay-uploader-setup.exe  simple installer - place next to rocket-replay-uploader.exe or .zip and run it
  installer.iss                     Inno Setup script (iscc installer.iss -> setup.exe)
  install.bat                       batch installer alternative
  readme.txt                        this file

Quick start (portable):
  unzip rocket-replay-uploader.zip and double-click rocket-replay-uploader.exe

Installer:
  1. keep rocket-replay-uploader-setup.exe next to rocket-replay-uploader.exe (or .zip)
  2. run rocket-replay-uploader-setup.exe
  3. it copies to %LOCALAPPDATA%\RocketReplayUploader, creates Start Menu shortcut, optional autostart
