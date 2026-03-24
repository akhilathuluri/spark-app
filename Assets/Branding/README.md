# Branding Assets

Runtime target format for this app is `.ico`.
Source/edit format is `.svg`.

## Folder structure

- `Assets/Branding/svg/`:
  - Keep source vector files (recommended master: `logo-master.svg` at `1024x1024`).
- `Assets/Branding/ico/`:
  - Keep exported multi-size icon as `App.ico`.

## Required ICO sizes

Export one `App.ico` containing these embedded sizes:

- 16
- 20
- 24
- 32
- 40
- 48
- 64
- 128
- 256

## Notes

- `Spark.csproj` is configured to use `Assets/Branding/ico/App.ico` as app icon when the file exists.
- `TrayService` uses `IconService.CreateAppTrayIcon()`, which now tries to load `App.ico` first and falls back to generated icon.
- After adding/replacing `App.ico`, rebuild the app to refresh EXE/taskbar/tray branding.
