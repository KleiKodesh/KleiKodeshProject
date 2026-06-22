# DocDesignLib — Torah Document Formatting WPF Library

WPF class library providing Torah document formatting tools. Loaded by `KleiKodeshVsto` and displayed in a task pane when the user clicks the **עיצוב תורני** ribbon button.

## Architecture

Strict MVVM. Each feature area has its own ViewModel (inheriting `ViewModelBase` from WpfLib). The main `DocDesignViewModel` is a thin coordinator that instantiates sub-ViewModels.

```
DocDesignView.xaml
  └── DocDesignViewModel
        ├── ParagraphsViewModel
        ├── ColumnsViewModel
        └── SpacingViewModel
              └── Vsto (static gateway) → Word Application/Selection/Document
```

## Files

### Root UI
- **`UI/DocDesignView.xaml` / `.cs`** — Root UserControl (task pane host). Creates the three section expanders and sets `DataContext = DocDesignViewModel`.
- **`UI/DocDesignViewModel.cs`** — Main ViewModel; instantiates sub-ViewModels. Coordinates all formatting operations.
- **`UI/DocDesignDictionary.xaml`** — Root resource dictionary; merges all theme files.

### Paragraphs (`Paragraphs/`)
- **`FirstWordStyle.cs`** — Applies a character style to the first word of selected paragraphs. Style picker filters document styles by usage (only shows styles actually used in the document).
- **`FirstWordHanging.cs`** — Creates hanging indent on first word (single or double column variants). Adjusts left indent and first-line indent.
- **`CenterLastLine.cs`** — Centers the last line of a paragraph via tab stops (preferred) or line break fallback.
- **`ParagraphsBase.cs`** — Shared base class: `ValidParagraphs` filter (skips headers/footnotes/tables), footnotes prep.
- **`ParagraphsViewModel.cs`** — ViewModel for the Paragraphs section.

### Columns (`Columns/`)
- **`AlignColumns.cs`** — Aligns two-column sections by iteratively adjusting paragraph spacing until column heights match.
- **`ColumnsHelper.cs`** — Range extension methods for page data, section boundaries, column break points.
- **`ColumnsViewModel.cs`** — ViewModel for the Columns section. "Find next uneven column pair" navigation.

### Spacing (`Spacing/`)
- **`SpacingHelper.cs`** — Selection extension methods that read current spacing values from the Word style or direct formatting.
- **`SpacingViewModel.cs`** — ViewModel for Spacing controls: space-after, space-before, line spacing (Single/1.5/Double/Exactly), word spacing, character stretch (scale). Each has +/−/reset buttons.

### Helpers (`Helpers/`)
- **`Vsto.cs`** — Static gateway to Word `Application`, `Selection`, `Document`. Set once by `DocDesignView` constructor. All operation classes read from it directly — never pass Word objects through method parameters.
- **`ScreenFreeze.cs`** — RAII wrapper (IDisposable): disables screen updates in constructor (`Application.ScreenUpdating = false`), re-enables in Dispose. Use in `using` blocks around bulk document operations.
- **`UndoRecord.cs`** — RAII wrapper: starts a named Word undo record in constructor, ends in Dispose. Groups all intermediate changes into a single Ctrl+Z step.
- **`RangePageData.cs`** — DTO: `FirstPage`, `LastPage`, `PageCount` for a `Word.Range`.

## Tools Provided

| Feature | What It Does | Key Method |
|---------|-------------|------------|
| **Paragraphs** | Apply/remove character style on first word; hanging indent (single/double); center last line | `FirstWordStyle.ApplyStyle()`, `FirstWordHanging.Apply()`, `CenterLastLine.Apply()` |
| **Columns** | Align two-column sections by iteratively adjusting spacing; find next uneven pair | `AlignColumns.Align()`, `ColumnsViewModel.FindNextUnevenPair()` |
| **Spacing** | Fine-tune space-after/before, line spacing, word spacing, character stretch | `SpacingHelper.ApplySpacing()`, `SpacingHelper.Reset()` |

## Key Patterns

- **RAII for safety** — `ScreenFreeze` and `UndoRecord` are used in `using` blocks around every document-touching operation. Never modify the Word document outside of these guards.
- **Static Vsto gateway** — `Vsto.cs` is set once by the View constructor. All code reads `Vsto.Application`, `Vsto.Selection`, `Vsto.Document` directly — no parameter passing.
- **Adaptive theme** — Uses `WpfLib/Themes/Brushes.xaml` mid-gray palette for automatic light/dark Office theme support.
- **Section expanders** — Each feature area is an `Expander` with title-bar styling. Only one expander open at a time.

## Visual Style

Matches `RegexFindLib` and `WebSitesLib` — same color tokens, `Segoe UI` font, icon button pattern (`Viewbox/Canvas/Path`). Section headers use a custom `Expander` template (chevron indicator, title-bar gradient).

## Running the Demo

Set `DocDesignDemo` as startup project and run. It hosts `DocDesignView` with no Word — all commands no-op gracefully since `Vsto` is null. Uncomment the dark-mode lines in `MainWindow.xaml.cs` to test dark theme.
