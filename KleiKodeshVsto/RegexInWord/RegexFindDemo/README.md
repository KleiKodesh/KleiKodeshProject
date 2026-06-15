# RegexFindDemo

Standalone WPF demo application for testing regex find & replace functionality.

## Purpose

Tests the RegexFindLib library independently of the Word VSTO context, using a mock Word document service.

## Components

- `MockWordService.cs` — Mock implementation of Word document interface for testing
- `MainWindow.xaml` / `MainWindow.xaml.cs` — Demo UI

## Usage

1. Open `RegexInWord.sln` in Visual Studio
2. Set `RegexFindDemo` as the startup project
3. Press F5 to run

Use this when developing regex features without needing Word open.
