# RegexFindLib

WPF class library providing the **Regex Find & Replace** task pane for KleiKodesh.
Replaces the original HTML/WebView2 frontend with a native WPF UI.

## Projects

| Project | Description |
|---|---|
| `RegexFindLib/` | The library — WPF UserControl + MVVM |
| `RegexFindDemo/` | Standalone WPF demo app for UI debugging (no Word required) |

## Architecture

Strict MVVM with injected `IWordService`:

```
View (XAML)
  └── RegexFindViewModel (partial: .cs / .Commands.cs / .Loading.cs)
        └── RegexSearch (model, injected IWordService)
              └── WordService → Vsto.Application (only touch point)
```

- **Model** (`Search/`) — `RegexSearch`, `RegexSearchModels` — no UI, no Vsto
- **ViewModel** (`UI/RegexFindViewModel*.cs`) — bindable state, commands, loading
- **View** (`UI/RegexFindView.xaml`) — XAML only, minimal code-behind
- **Infrastructure** (`Helpers/`) — `WordService`, `WdActionManager`, `Vsto` gateway

## Shared vs Per-Instance State

| State | Scope | Reason |
|---|---|---|
| `FontList` | `static` | System fonts don't change; loaded once async |
| `RecentSearches/Replacements` | `static` | All panes share history |
| `SearchModes` | `static` | Fixed labels |
| `StyleList` | Per-instance | Document-specific, filtered by `InUse` |
| Search/replace text, results, formatting | Per-instance | Each pane is independent |

## Themes

Styles split by concern under `UI/Themes/`:

| File | Contents |
|---|---|
| `Icons.xaml` | `StreamGeometry` resources from `@iconify-prerendered/vue-fluent` |
| `Brushes.xaml` | Office Fluent 2 color tokens |
| `ButtonStyles.xaml` | Icon buttons, toggles, title bar toggle |
| `FormatToggle.xaml` | Three-state `CheckBox IsThreeState` format toggle |
| `SearchComboStyle.xaml` | Borderless search/replace `ComboBox` with placeholder |
| `ComboBoxStyles.xaml` | `OfficeComboItem` + implicit Office `ComboBox` style |
| `MiscStyles.xaml` | Input wrapper, result item, Edge-style scrollbar |

## Entry Point

```csharp
// From VSTO ribbon (KeliKodeshRibbon.cs):
var view = new RegexFindLib.UI.RegexFindView(
    Globals.ThisAddIn.Application,
    Globals.Factory);
WpfTaskPane.Show(view, "חיפוש רגקס", 600);

// From demo app (no Word required):
var view = new RegexFindView(new MockWordService());
```

## Demo App

Build and run `RegexFindDemo` to iterate on the UI without launching Word:

```
MSBuild RegexFind\RegexFindDemo\RegexFindDemo.csproj
RegexFind\RegexFindDemo\bin\Debug\RegexFindDemo.exe
```
