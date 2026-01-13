---
inclusion: fileMatch
fileMatchPattern: '**/ZayitWrapper/**'
---

# ZayitWrapper - Standalone Zayit Viewer

## Overview
Standalone Windows Forms application that wraps ZayitLib to provide a desktop Hebrew books viewer without requiring Microsoft Word or VSTO.

## Features
- **WebView2 Integration**: Modern web rendering with Microsoft WebView2
- **Hebrew Books Support**: Full integration with HebrewBooks.org database
- **PDF Viewing**: Built-in PDF.js integration for Hebrew texts
- **Search Functionality**: Advanced search capabilities across Hebrew texts
- **High DPI Support**: Optimized for high DPI displays with per-monitor DPI awareness
- **Dialog Fix**: Resolved freezing issues with file dialogs and Windows dialogs

## Requirements
- .NET Framework 4.7.2 or higher
- Microsoft WebView2 Runtime (auto-installed on Windows 11, available for Windows 10)
- Windows 10 version 1803 or later

## Architecture
- **MainForm**: Main Windows Forms container
- **ZayitViewerHost**: UserControl wrapper for WebView component
- **ZayitViewer**: WebView2 component with C# ↔ JavaScript bridge
- **ZayitViewerCommands**: Command handlers for JavaScript interactions

## Dependencies
- **ZayitLib**: Core library with all Zayit functionality
- **Microsoft.Web.WebView2**: WebView2 control for Windows Forms
- **System.Text.Json**: JSON serialization for C# ↔ JavaScript communication

## Communication Pattern
Uses established Zayit bridge pattern:
1. **JavaScript → C#**: Commands via `window.chrome.webview.postMessage()`
2. **C# → JavaScript**: Responses via `CoreWebView2.PostWebMessageAsString()`
3. **Command Dispatch**: Reflection-based method invocation in ZayitViewerCommands

## High DPI Configuration
- **Application Manifest**: `app.manifest` with PerMonitorV2 DPI awareness
- **Program.cs**: P/Invoke calls to `SetProcessDpiAwareness`
- **Form Settings**: `AutoScaleMode.Dpi` for proper scaling

## Dialog Handling
- **WebViewDialogHelper**: Prevents dialog freezing with async handling
- **Proper Threading**: Uses `BeginInvoke` instead of blocking `Invoke`
- **Parent Window Management**: Correct dialog parenting to prevent UI conflicts

## File Structure
```
ZayitWrapper/
├── MainForm.cs              # Main application window
├── MainForm.Designer.cs     # Form designer code
├── Program.cs               # Application entry point with DPI support
├── app.manifest             # DPI awareness configuration
├── Properties/              # Assembly info and resources
└── ZayitWrapper.csproj      # Project file with SQLite targets
```

## Build Process
1. Vue application builds automatically via pre-build event
2. HTML files copied to ZayitLib/Html during build
3. SQLite native libraries copied to output directory
4. All dependencies resolved via NuGet packages

## Troubleshooting

### SQLite DLL Missing
- **Fixed**: SQLite.Interop.dll now automatically copied during build
- **Solution**: Added SQLite MSBuild targets to project file

### WebView2 Not Available
- Install Microsoft Edge WebView2 Runtime
- Ensure Windows 10 version 1803 or later

### Dialog Freezing
- **Fixed**: All dialogs use WebViewDialogHelper for async handling
- **Solution**: Proper threading prevents WebView2 message pump blocking

### JavaScript Errors
- Open Developer Tools (F12) to see console
- Verify Vue application built successfully

## Development Notes
- Ensure Vue app builds before running C# application
- Use `WebViewDialogHelper` for any new dialogs
- Follow established bridge pattern for C# ↔ JavaScript communication
- Test on high DPI displays to verify scaling behavior