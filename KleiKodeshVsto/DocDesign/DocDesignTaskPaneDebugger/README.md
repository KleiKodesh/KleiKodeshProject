# DocDesignTaskPaneDebugger

Standalone WPF debugger application for testing Torah document formatting.

## Purpose

Tests the DocDesignTaskPane formatting library independently of the Word VSTO context.

## Files

| File | Description |
|------|-------------|
| `App.xaml` / `App.xaml.cs` | Application definition and startup |
| `MainWindow.xaml` / `.cs` | Main debugger window |

## Usage

1. Open the solution in Visual Studio
2. Set `DocDesignTaskPaneDebugger` as the startup project
3. Press F5 to run

Use this when developing formatting features without needing Word open.
