# Common

Shared components for KleiKodesh VSTO add-in.

## Files

- `KleiKodeshWebView.cs` — Custom WebView2 wrapper for hosting the KitveiHakodesh Vue frontend
  - Manages WebView initialization
  - Provides message bridge for Vue/C# communication
  - Handles host object for JavaScript interop
