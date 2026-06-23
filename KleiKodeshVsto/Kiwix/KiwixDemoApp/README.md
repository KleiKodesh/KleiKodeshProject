# KiwixDemoApp

Standalone WinForms demo application for testing the Kiwix ZIM file reader.

## Purpose

Tests the KiwixLib library independently of the Word VSTO context.

## Usage

1. Open `Kiwix.slnx` in Visual Studio
2. Set `KiwixDemoApp` as the startup project
3. Press F5 to run

## Files

| File | Description |
|------|-------------|
| `MainForm.cs` | Main window with ZIM loading, navigation, and search UI |
| `Program.cs` | Application entry point |

## Features

- Load and browse ZIM files
- Full-text search within offline content
- No Word/Office required

Use this when developing or debugging Kiwix functionality without needing Word open.
