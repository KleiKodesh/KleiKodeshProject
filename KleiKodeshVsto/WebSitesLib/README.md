# WebSitesLib

WPF library for curated website browser with WebView2 integration.

## Projects

### WebSitesLib
The main library containing:
- `WebSitesView` — Main UserControl with tabbed browser interface
- `BrowserTabControl` — Custom tab control for managing multiple browser tabs
- `MyWebView` — WebView2 wrapper component
- `WebAddressModel` — Model for website entries
- `WebSitesWhitelist.json` — Default list of curated websites

### WebSitesDemo
Standalone WPF demo application that hosts the WebSitesView control. Use this to test and develop the library independently of the VSTO add-in.

**To run the demo:**
1. Open `WebSitesLib.sln` in Visual Studio
2. Set `WebSitesDemo` as the startup project
3. Press F5

## Integration with KleiKodesh VSTO

The library is referenced by `KleiKodeshVsto.csproj` and displayed as a task pane when the user clicks the "דרך האתרים" ribbon button.

**Ribbon integration:**
```csharp
// In KeliKodeshRibbon.cs
case "WebSites":
    WpfTaskPane.Show(new WebSitesLib.WebSitesView(), "דרך האתרים", 510);
    break;
```

## Whitelist Management

The installer embeds `WebSitesWhitelist.json` and extracts it to the user's installation directory on every install or update. Users can customize the list before installation via the Advanced page in the installer.

**How it works:**
- The source JSON contains all entries with `IsVisible` flags — the full catalogue
- The installer dialog shows all entries with checkboxes
- On OK, only the checked entries are written to disk (no `IsVisible` field in the output)
- The VSTO add-in loads whatever is in the file and shows all of it — no filtering

See `Build/Installer/README.md` for full details.

## Dependencies

- **WpfLib** — Shared WPF utilities (ViewModelBase, helpers, attached properties)
- **Microsoft.Web.WebView2** — Chromium-based web browser control
- **GongSolutions.WPF.DragDrop** — Drag-and-drop support for reordering tabs
- **System.Text.Json** — JSON serialization for whitelist

## File Structure

```
WebSitesLib/
├── WebSitesLib/              # Main library
│   ├── BrowserTabControl.cs
│   ├── MyWebView.cs
│   ├── WebAddressModel.cs
│   ├── WebSitesView.xaml
│   ├── WebSitesView.xaml.cs
│   ├── WebSitesWhitelist.json
│   └── Dictionary1.xaml      # Resource dictionary
├── WebSitesDemo/             # Demo application
│   ├── App.xaml
│   ├── MainWindow.xaml
│   └── Properties/
└── WebSitesLib.sln
```

## History

Previously named `WebSitesLib2` (the "2" was a remnant from an earlier refactoring). Renamed to `WebSitesLib` in April 2026 to remove confusion.
