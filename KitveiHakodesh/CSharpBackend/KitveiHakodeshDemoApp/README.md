# KitveiHakodeshDemoApp

Standalone WinForms demo application for testing the KitveiHakodeshLib backend.

## Purpose

Hosts the KitveiHakodeshLib WebView2 control and Vue frontend outside of the Word VSTO context for development and testing. `MainForm.cs` is a plain WinForms shell that hosts a single `AppViewer` UserControl — all app UI (tabs, panes, pages) lives in the Vue frontend.

## Usage

1. Open `KitveiHakodesh.slnx` in Visual Studio
2. Set `KitveiHakodeshDemoApp` as startup project
3. Press F5 to run

## Features

- Full KitveiHakodesh functionality without Word
- SQLite database access
- Ftslib search engine
- PDF and HTML viewers
- Theme and settings

Use this when debugging the backend or frontend without needing to restart Word/Office.
