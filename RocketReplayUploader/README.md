# Rocket Replay Uploader

A local Rocket League replay manager connected to the [ballchasing.com](https://ballchasing.com) API. It watches your replays folder, **auto-uploads every replay you save**, and lets you manage all your replays from a modern desktop app (with a replay-group creator).

Built with WPF / .NET 9. Windows only.

## Download

**No installation needed** — the published app is a single portable `.exe` (self-contained, includes .NET):

👉 [**Download RocketReplayUploader.exe**](https://github.com/Maaariioo/RocketReplayUploader/releases/latest)

You can also grab the same builds straight from this repository: `builds/rocket-replay-uploader.exe` (single-file) and `builds/rocket-replay-uploader-dlls.zip` (exe + dlls)

Double-click it and set it up. Everything else is optional.

## How it works

### 1. Auto-upload (the main feature)

- The app watches the folder where Rocket League saves replays (`Documents\My Games\Rocket League\TAGame\Demos` by default).
- The **Auto-upload** toggle is ON by default: every time Rocket League saves a replay, the app **uploads it to ballchasing.com automatically** and **renames the file** to a `Player_Mode_Game_Date` pattern.
- Uploads are queued and retried when they fail (network problems, Ballchasing being slow, etc.). Done items can be sent to the recycle bin or moved to an archive folder.

### 2. Replay manager window

The main window lists all your replays (parsed from the `.replay` file header: mode, map, score, date). Each row has buttons to:

- **Rename** — normalize the filename.
- **Upload** — upload it now and set its title on Ballchasing.
- **View** — open it on ballchasing.com (`/r/<id>`).
- **Delete** — remove the file (with confirmation).

There are also batch actions: **Rename all**, **Upload all**, and **Delete all**.

### 3. Replay group creator

Select the replays you want, click **Create group**, give the group a name and your player identification (Steam/Epic), optionally identify teams — the app creates the group on Ballchasing and assigns the selected replays to it. No need to do it by hand on the website.

### 4. Background operation

Closing the window keeps the app running in the **system tray**. Right-click the tray icon to reopen the window, toggle auto-upload, or quit completely.

## Interface

- Dark / light themes.
- **Language selector** with live switching (no restart): English, Español, Français.
- Minimize-to-tray with notifications when uploads finish.

## First run

1. Launch `RocketReplayUploader.exe`.
2. The setup window opens:
   - **Replays folder** — where Rocket League saves replays (the usual path is pre-filled; use **Detect folders** to auto-find it).
   - **Your player name.**
   - **Ballchasing API key** — validated online the moment you save it.
   - Upload visibility: public / unlisted / private.
   - Whether to start the app automatically when you log in.

3. That's it — from then on it runs in the tray and auto-uploads.

Your configuration (including the encrypted API key) is stored at:

```
%AppData%\RocketReplayUploader\config.json
```

## Manual commands

```
RocketReplayUploader.exe --setup          Open the configuration window
RocketReplayUploader.exe --install        Start automatically at login (recommended)
RocketReplayUploader.exe --uninstall      Remove auto-start
RocketReplayUploader.exe --install-service    Real background Windows service (run as Administrator)
RocketReplayUploader.exe --uninstall-service  Remove the service
```

## Building from source

Requires the **.NET 9 SDK**.

```
dotnet restore RocketReplayUploader.sln
dotnet test  RocketReplayUploader.sln        # 58 tests
dotnet build RocketReplayUploader.sln -c Release
```

Publish the single-file portable exe:

```
dotnet publish RocketReplayUploader/RocketReplayUploader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The exe is written to `RocketReplayUploader\bin\Release\net9.0-windows\win-x64\publish\RocketReplayUploader.exe`.

## Tech notes

- WPF (net9.0-windows), MVVM, hosted services for the folder watcher and upload queue.
- Ballchasing API client with transient-error retry logic (C# `HttpClient`).
- API key encrypted at rest with Windows DPAPI (`SecretProtector`), never stored in plain text.
- Localization via RESX resources (`Resources\Strings.*.resx`) + a `TranslationSource` that raises `PropertyChanged` for instant language swap.