# WebSitesLib

WPF class library for the website browser component.

## File Structure

All source files are under the `UI/` subdirectory.

### UI/ — Controls and Models

| File | Description |
|------|-------------|
| `WebSitesView.xaml` / `.cs` | Main WPF UserControl with tabbed browser interface |
| `BrowserTabControl.cs` | Custom tab control for managing multiple browser tabs |
| `MyWebView.cs` | WebView2 wrapper component |
| `WebAddressModel.cs` | Data model for website entries |
| `WebSitesDictionary.xaml` | Resource dictionary with styles |

### UI/Themes/ — Style Resources

| File | Contents |
|------|----------|
| `Icons.xaml` | Icon geometry resources |
| `Brushes.xaml` | Color tokens |
| `ButtonStyles.xaml` | Button styles |
| `AddressBarStyles.xaml` | Address bar styling |
| `MiscStyles.xaml` | Miscellaneous styles |

### Root Files

| File | Description |
|------|-------------|
| `WebSitesLib.csproj` | Project file |
| `packages.config` | NuGet package references |
| `WebSitesWhitelist.json` | Configuration file listing available websites |

## Integration

This library is packaged as a task pane displayed when the user clicks the "דרך האתרים" ribbon button in the VSTO add-in.

```csharp
WpfTaskPane.Show(new WebSitesLib.UI.WebSitesView(), "דרך האתרים", 510);
```

## Dependencies

- **Microsoft.Web.WebView2** — Chromium-based web browser control
- **GongSolutions.WPF.DragDrop** — Drag-and-drop support for reordering tabs
- **System.Text.Json** — JSON serialization for whitelist
