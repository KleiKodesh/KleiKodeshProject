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
- `WebSitesDictionary.xaml` — Resource dictionary with styles

All source files live under the `UI/` subdirectory.

**Theme files** (`UI/Themes/`):
| File | Contents |
|------|----------|
| `Icons.xaml` | Icon geometry resources |
| `Brushes.xaml` | Color tokens |
| `ButtonStyles.xaml` | Button styles |
| `AddressBarStyles.xaml` | Address bar styling |
| `MiscStyles.xaml` | Miscellaneous styles |

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
case "WebSites":
    WpfTaskPane.Show(new WebSitesLib.WebSitesView(), "דרך האתרים", 510);
    break;
```

## Whitelist Management

The installer embeds `WebSitesWhitelist.json` and extracts it to the user's installation directory on every install or update. Users can customize the list before installation via the Advanced page in the installer.

**How it works:**
- User never opens the dialog → whitelist untouched (existing file preserved on update, default extracted on fresh install)
- User opens the dialog → full catalogue shown; each entry pre-checked based on the installed file (present = checked, absent = unchecked; fresh install uses default `IsVisible`)
- On OK → only checked entries written to disk, no `IsVisible` field in output
- The VSTO add-in loads whatever is on disk and shows all of it — no filtering

See `Build/Installer/README.md` for full details.

## Dependencies

- **WpfLib** — Shared WPF utilities (ViewModelBase, helpers, attached properties)
- **Microsoft.Web.WebView2** — Chromium-based web browser control
- **GongSolutions.WPF.DragDrop** — Drag-and-drop support for reordering tabs
- **System.Text.Json** — JSON serialization for whitelist

## File Structure

```
WebSitesLib/
├── WebSitesLib.sln
├── WebSitesLib/                  # Main library
│   ├── WebSitesLib.csproj
│   ├── packages.config
│   ├── WebSitesWhitelist.json
│   ├── UI/
│   │   ├── BrowserTabControl.cs
│   │   ├── MyWebView.cs
│   │   ├── WebAddressModel.cs
│   │   ├── WebSitesView.xaml
│   │   ├── WebSitesView.xaml.cs
│   │   ├── WebSitesDictionary.xaml
│   │   └── Themes/
│   │       ├── Icons.xaml
│   │       ├── Brushes.xaml
│   │       ├── ButtonStyles.xaml
│   │       ├── AddressBarStyles.xaml
│   │       └── MiscStyles.xaml
│   └── Properties/
├── WebSitesDemo/                 # Demo application
│   ├── WebSitesDemo.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / .cs
│   └── Properties/
├── packages/
└── README.md
```

## History

Previously named `WebSitesLib2` (the "2" was a remnant from an earlier refactoring). Renamed to `WebSitesLib` in April 2026 to remove confusion.
