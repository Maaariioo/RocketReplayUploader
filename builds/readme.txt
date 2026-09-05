Rocket Replay Uploader - portable builds

Files in this folder:
  rocket-replay-uploader.exe        single-file portable exe (self-contained, no install needed, just double-click)
  rocket-replay-uploader-dlls.zip   same app but unpacked (exe + dlls), for users who prefer dll distribution
  installer.iss                     Inno Setup script to build a real installer (run: iscc installer.iss)
  install.bat                       simple copy installer (copies exe to %LOCALAPPDATA%\RocketReplayUploader)
  readme.txt                        this file

Quick start:
  double-click rocket-replay-uploader.exe

To build an installer exe from the iss script:
  winget install JRSoftware.InnoSetup
  iscc builds\installer.iss
  -> creates builds\rocket-replay-uploader-setup.exe
