# RegexFindLib

WPF class library providing the **Regex Find & Replace** task pane for KleiKodesh.
Replaces the original HTML/WebView2 frontend with a native WPF UI.

## Projects

| Project | Description |
|---------|-------------|
| `RegexFindLib/` | The library — WPF UserControl + MVVM |
| `RegexFindDemo/` | Standalone WPF demo app for UI debugging (no Word required) |

## Architecture

Strict MVVM with injected `IWordService`:

```
View (XAML)
  └── RegexFindViewModel (partial: .cs / .Commands.cs / .Loading.cs / .Palette.cs)
        └── RegexSearch (model, injected IWordService)
              └── WordService → Vsto.Application (only touch point)
```

- **Model** (`Search/`) — `RegexSearchMain`, `RegexSearchFind`, `RegexSearchReplace`, `SearchModels` — no UI, no Vsto
- **ViewModel** (`UI/RegexFindViewModel*.cs`) — bindable state, commands, loading, palette
- **View** (`UI/RegexFindView.xaml`) — XAML only, minimal code-behind
- **Infrastructure** (`Helpers/`) — `WordService`, `WdActionManager`, `Vsto`, `FileLogger`

## File Listing

### Search/ — Search Engine

| File | Description |
|------|-------------|
| `ISearchEngine.cs` | Search engine interface |
| `IWordService.cs` | Word document abstraction interface |
| `RegexSearchMain.cs` | Main search/replace orchestration |
| `RegexSearchFind.cs` | Find logic implementation |
| `RegexSearchReplace.cs` | Replace logic implementation |
| `SearchModels.cs` | Search result and criteria models |
| `WordSearchEngine.cs` | Concrete search engine implementation |

### UI/ — ViewModel and View

| File | Description |
|------|-------------|
| `RegexFindView.xaml` / `.cs` | Main WPF UserControl |
| `RegexFindViewModel.cs` | ViewModel (shared state) |
| `RegexFindViewModel.Commands.cs` | Command definitions |
| `RegexFindViewModel.Loading.cs` | Async loading logic |
| `RegexFindViewModel.Palette.cs` | Color palette logic |
| `RegexFindDictionary.xaml` | Resource dictionary |
| `Converters.cs` | XAML value converters |
| `ColorPickerButton.xaml.cs` | Color picker button control |
| `FormatOptionsRow.xaml.cs` | Formatting options row control |
| `FormattingOptions.cs` | Formatting options model |
| `FontItem.cs` | Font selection item model |
| `PlaceholderBehavior.cs` | Placeholder text behavior |
| `RegexPalettePanel.cs` | Color palette panel |
| `RegexTipBehavior.cs` | Tooltip behavior |
| `SearchHistory.cs` | Search history manager |
| `SnippetBlock.cs` | Search result snippet block |
| `SnippetModel.cs` | Search result snippet model |
| `SpaceBetweenPanel.cs` | Panel layout helper |
| `SpinnerTextBox.xaml.cs` | TextBox with loading spinner |
| `WordColors.cs` | Word color constants |

### UI/Themes/ — Style Resources

| File | Contents |
|------|----------|
| `Icons.xaml` | `StreamGeometry` resources from `@iconify-prerendered/vue-fluent` |
| `Brushes.xaml` | Office Fluent 2 color tokens |
| `ButtonStyles.xaml` | Icon buttons, toggles, title bar toggle |
| `FormatToggle.xaml` | Three-state `CheckBox IsThreeState` format toggle |
| `FormatOptionsRowStyles.xaml` | Styles for format options row |
| `ComboBoxStyles.xaml` | `OfficeComboItem` + implicit Office `ComboBox` style |
| `ColorPickerStyles.xaml` | Styles for color picker control |
| `PaletteStyles.xaml` | Styles for color palette panel |
| `SpinnerTextBoxStyles.xaml` | Styles for spinner text box |
| `MiscStyles.xaml` | Input wrapper, result item, Edge-style scrollbar |

### Helpers/ — Infrastructure

| File | Description |
|------|-------------|
| `WordService.cs` | Word interop adapter implementing `IWordService` |
| `WdActionManager.cs` | Word action (undo) management |
| `Vsto.cs` | VSTO application gateway |
| `FileLogger.cs` | Debug logging utility |

## Shared vs Per-Instance State

| State | Scope | Reason |
|-------|-------|--------|
| `FontList` | `static` | System fonts don't change; loaded once async |
| `RecentSearches/Replacements` | `static` | All panes share history |
| `SearchModes` | `static` | Fixed labels |
| `StyleList` | Per-instance | Document-specific, filtered by `InUse` |
| Search/replace text, results, formatting | Per-instance | Each pane is independent |

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
MSBuild KleiKodeshVsto\RegexInWord\RegexFindDemo\RegexFindDemo.csproj
KleiKodeshVsto\RegexInWord\RegexFindDemo\bin\Debug\RegexFindDemo.exe
```
