# Common — Shared VSTO Components

Shared components for the VSTO Word add-in, currently a single file.

## Files

### `KleiKodeshWebView.cs`
Custom WebView2 wrapper that hosts the KitveiHakodesh Vue frontend inside a Word task pane.

Key responsibilities:
- **Initialization** — Creates `Microsoft.Web.WebView2.Wpf.WebView2` control, ensures CoreWebView2 environment is ready
- **Message bridge** — Provides `window.__webviewQuery` and `window.__webviewAction` JavaScript functions that the Vue app calls for database queries and C# operations
- **Host object** — Registers .NET host object for JS interop via `CoreWebView2.AddHostObjectToScript()`
- **Navigation** — Loads the KitveiHakodesh Vue app from embedded/local HTML
- **Dev mode** — Supports navigating to the Vite dev server URL when `DEBUG` is defined

Key methods:
- `NavigateToApp()` — Loads the Vue frontend
- `InvokeScriptAsync(string script)` — Injects JavaScript into WebView2
- `OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs)` — Handles messages from Vue app
- `RegisterBridge()` — Sets up the C#/JS communication bridge

Instantiated by `KitveiHakodeshLib.AppViewer` when the user clicks the "כתבי הקודש" ribbon button.
