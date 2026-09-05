# Rocket Replay Uploader

Local Rocket League replay manager for Windows. Watches your replays folder and **auto-uploads every replay you save to [ballchasing.com](https://ballchasing.com)** — plus a replay manager and group creator so you don't have to do it by hand.

Built with WPF / .NET 9. No account needed except your Ballchasing API key.

## Download

**No install needed** — just unzip and double-click:

👉 **[Download rocket-replay-uploader.zip (latest release)](https://github.com/Maaariioo/RocketReplayUploader/releases/latest)**

Unzip it and run `rocket-replay-uploader.exe`. On first launch it asks for your replays folder, player name and Ballchasing API key — then it runs in the system tray and auto-uploads.

> All releases: https://github.com/Maaariioo/RocketReplayUploader/releases

## What it does

- **Auto-upload** — the Auto-upload toggle is on by default. Every time Rocket League saves a replay, the app uploads it to Ballchasing automatically and renames the file to `Player_Mode_Game_Date`. Failed uploads are queued and retried.
- **Replay manager** — lists all your replays (mode, map, score, date parsed from the `.replay` header) with per-row **Rename / Upload / View / Delete** and batch actions.
- **Group creator** — select replays, click *Create group*, give it a name and your player ID — the app creates the group on Ballchasing and assigns the replays to it.
- **Runs in the background** — closing the window keeps it in the system tray. Dark/light themes, and **English / Español / Français** with live switching.

## Project layout

- `RocketReplayUploader/` — the app (WPF)
- `RocketReplayUploader.Tests/` — tests (58 tests)
- Detailed setup, commands and build instructions: see [`RocketReplayUploader/README.md`](RocketReplayUploader/README.md)

## Building from source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet test RocketReplayUploader/RocketReplayUploader.sln
dotnet publish RocketReplayUploader/RocketReplayUploader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# output: RocketReplayUploader/bin/Release/net9.0-windows/win-x64/publish/RocketReplayUploader.exe
```

Config is stored at `%AppData%\RocketReplayUploader\config.json` (API key encrypted with Windows DPAPI).
