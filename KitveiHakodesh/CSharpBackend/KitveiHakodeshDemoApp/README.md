# KitveiHakodeshDemoApp

Standalone WinForms demo application for testing the KitveiHakodeshLib backend.

## Purpose

Hosts the KitveiHakodeshLib WebView2 control and Vue frontend outside of the Word VSTO context for development and testing. `MainForm.cs` is a plain WinForms shell that hosts a single `AppViewer` UserControl — all app UI (tabs, panes, pages) lives in the Vue frontend.

## Usage

1. Open `KitveiHakodesh.slnx` in Visual Studio
2. Set `KitveiHakodeshDemoApp` as startup project
3. Press F5 to run

## Command Line

`argv[1]` is the one argument the app takes, and it may be either of two things:

- **a file path** — opened in a tab (this is what the "Open with" shell registration passes)
- **a deep link** — `kitveihakodeshapp://book/<bookId>?index=<lineIndex>`, the format
  `buildLineLink` in `vue-frontend/src/utils/appDeepLink.ts` produces and
  `KitveiHakodeshLib/HostLink.cs` parses. Windows passes it here once the scheme is registered
  for this exe; the registration lives in `Build/Installer/README.md`. The other two families
  `HostLink` parses (`otzaria://`, `zayit://`) are accepted here too and open the same way.

Both go through `MainForm.OpenRequest`, and both obey the single-instance rule: the first
instance holds `MutexName` and listens on `PipeName`; every later launch forwards its argument
over the pipe and exits, so the request becomes a **new tab in the running window** rather than
a second window. Unrecognised arguments are ignored, which is how `--plain` stays a flag.

## Features

- Full KitveiHakodesh functionality without Word
- SQLite database access
- Ftslib search engine
- PDF and HTML viewers
- Theme and settings

Use this when debugging the backend or frontend without needing to restart Word/Office.
