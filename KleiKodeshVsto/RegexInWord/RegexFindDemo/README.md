# RegexFindDemo

Standalone WPF demo application for testing regex find & replace functionality.

## Purpose

Tests the RegexFindLib library independently of the Word VSTO context, using a mock Word document service.

## Files

| File | Description |
|------|-------------|
| `App.xaml` / `App.xaml.cs` | Application definition and startup |
| `MainWindow.xaml` / `.cs` | Demo UI |
| `MockWordService.cs` | Mock implementation of Word document interface for testing |

## Usage

1. Open the solution in Visual Studio
2. Set `RegexFindDemo` as the startup project
3. Press F5 to run

Use this when developing regex features without needing Word open.
