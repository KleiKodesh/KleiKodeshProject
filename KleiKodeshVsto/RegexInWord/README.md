# RegexInWord — Regex Find & Replace

Regex find & replace functionality for Word, displayed as a WPF task pane.

## Projects

| Project | Description |
|---------|-------------|
| `RegexFindLib/` | WPF class library — UserControl + MVVM + search model |
| `RegexFindDemo/` | Standalone WPF demo app for UI debugging (no Word required) |

## Architecture (RegexFindLib)

Strict MVVM with injected `IWordService` interface:

```
View (RegexFindView.xaml) — XAML only, minimal code-behind
  └── RegexFindViewModel (.cs / .Commands.cs / .Loading.cs)
        └── RegexSearch (model, injected IWordService)
              └── WordService → Vsto.Application (only Word touch point)
```

### Layers

**Model** (`Search/`):
- `RegexSearch.cs` — Core search logic: pattern compile, match iterating, replace with capture groups. No UI, no Vsto dependency.
- `RegexSearchModels.cs` — Data models: `SearchResult`, `ReplaceResult`, `SearchOptions` (case sensitivity, whole word, wildcards).

**ViewModel** (`UI/RegexFindViewModel*.cs`):
- `RegexFindViewModel.cs` — Bindable state (search text, replace text, results, status)
- `RegexFindViewModel.Commands.cs` — Partial: command definitions (SearchCommand, ReplaceCommand, ReplaceAllCommand, ClearCommand)
- `RegexFindViewModel.Loading.cs` — Partial: loading states, busy indicator, cancellation

**View** (`UI/RegexFindView.xaml` + `.cs`):
- XAML layout: search input, replace input, results list, format toolbar, options panel
- Code-behind: minimal — wires ViewModel, handles focus

**Infrastructure** (`Helpers/`):
- `WordService.cs` — Implements `IWordService`; wraps Word Interop (Application, Selection, Document)
- `WdActionManager.cs` — Word document actions (insert, replace, format)
- `Vsto.cs` — Static gateway to Vsto.Application (set once by view constructor during VSTO mode)

### State Scoping

| State | Scope | Reason |
|-------|-------|--------|
| `FontList` | `static` | System fonts don't change; loaded once async |
| `RecentSearches` / `RecentReplacements` | `static` | All panes share history |
| `SearchModes` | `static` | Fixed labels/options |
| `StyleList` | Per-instance | Document-specific, filtered by `InUse` |
| Search/replace text, results, formatting | Per-instance | Each pane is independent |

## Integration

```csharp
// From VSTO ribbon (KeliKodeshRibbon.cs -> "RegexFind" tag):
var view = new RegexFindLib.UI.RegexFindView(
    Globals.ThisAddIn.Application,
    Globals.Factory);
WpfTaskPane.Show(view, "חיפוש רגקס", 600);

// From demo app (no Word required, uses mock):
var view = new RegexFindView(new MockWordService());
```

## Themes (RegexFindLib/UI/Themes/)

| File | Contents |
|------|----------|
| `Icons.xaml` | StreamGeometry icon resources (ported from @iconify-prerendered/vue-fluent) |
| `Brushes.xaml` | Office Fluent 2 adaptive color tokens |
| `ButtonStyles.xaml` | Icon buttons (`InputIconButton`, `TitleToggle`, `FormatButton`, etc.) |
| `FormatToggle.xaml` | Three-state CheckBox with red diagonal line for "excluded" state |
| `SearchComboStyle.xaml` | Borderless search/replace ComboBox with placeholder text |
| `ComboBoxStyles.xaml` | OfficeComboItem + implicit Office ComboBox style |
| `MiscStyles.xaml` | Input wrapper, result item, Edge-style scrollbar |

## Running the Demo

```powershell
msbuild KleiKodeshVsto\RegexInWord\RegexFindDemo\RegexFindDemo.csproj
.\KleiKodeshVsto\RegexInWord\RegexFindDemo\bin\Debug\RegexFindDemo.exe
```

The demo uses `MockWordService` that operates on an in-memory string — no Word installation required.

## Key Rules

- Never add Vsto dependency to the Model layer (`Search/`). Only `WordService` references Vsto.
- New search features (lookahead, replacement patterns) go in `RegexSearch.cs`.
- New UI elements go in the ViewModel, not the code-behind.
