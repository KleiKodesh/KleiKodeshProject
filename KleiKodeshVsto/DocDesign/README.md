# DocDesign — Torah Document Formatting Library (עיצוב תורני)

A WPF class library providing formatting tools tailored for Hebrew Torah documents. Loaded by `KleiKodeshVsto` and displayed in a task pane when the user clicks the **עיצוב תורני** ribbon button.

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `DocDesignLib/` | Class library | The actual library — all logic and UI |
| `DocDesignDemo/` | WPF exe | Standalone debug host — no Word/VSTO needed |

## How It Integrates with the VSTO Add-in

`KleiKodeshVsto` instantiates `DocDesign.DocDesignView(app, factory)`, passing the Word `Application` and VSTO `ApplicationFactory`. The view is hosted in a WPF task pane via `WpfTaskPane.Show()`. All document manipulation goes through the Word Interop API.

## Running the Demo

Open `DocDesignDemo` as the startup project and run. It hosts `DocDesignView` with no Word installation required — all commands will no-op gracefully since `Vsto` is null. Uncomment the dark-mode lines in `MainWindow.xaml.cs` to test the dark Office theme.

## Folder Structure

```
DocDesign/
├── DocDesignLib/               — Class library
│   ├── UI/
│   │   ├── DocDesignView.xaml/.cs     — Root UserControl (task pane UI)
│   │   ├── DocDesignViewModel.cs      — Main ViewModel; coordinates sub-ViewModels
│   │   ├── DocDesignDictionary.xaml   — Root resource dictionary (merges Themes/)
│   │   └── Themes/
│   │       ├── Brushes.xaml           — Adaptive mid-gray color tokens
│   │       ├── ButtonStyles.xaml      — Icon button, reset, increase/decrease styles
│   │       ├── MiscStyles.xaml        — TextBlock, CheckBox, ComboBox, scrollbar, etc.
│   │       └── ExpanderStyles.xaml    — Section header expander (title-bar style)
│   ├── Columns/
│   │   ├── AlignColumns.cs            — Aligns two-column sections by adjusting spacing
│   │   ├── ColumnsHelper.cs           — Range extension methods (page data, sections, break points)
│   │   └── ColumnsViewModel.cs        — ViewModel for the Columns section
│   ├── Paragraphs/
│   │   ├── FirstWordStyle.cs          — Applies a character style to the first word
│   │   ├── FirstWordHanging.cs        — Hanging indent on first word (single/double)
│   │   ├── CenterLastLine.cs          — Centers the last line via tab stops or line break
│   │   ├── PargaraphsBase.cs          — Shared base: ValidParagraphs filter, footnote prep
│   │   └── ParagraphsViewModel.cs     — ViewModel for the Paragraphs section
│   ├── Spacing/
│   │   ├── SpacingHelper.cs           — Selection extension methods (read spacing from style)
│   │   └── SpacingViewModel.cs        — ViewModel for the Spacing section
│   └── Helpers/
│       ├── Vsto.cs                    — Static gateway to Word Application/Selection/Document
│       ├── ScreenFreeze.cs            — RAII: disables screen updates during bulk operations
│       ├── UndoRecord.cs              — RAII: wraps operations in a named Word undo record
│       └── RangePageData.cs           — DTO: FirstPage, LastPage, PageCount for a Range
└── DocDesignDemo/              — Standalone WPF debug host
    ├── MainWindow.xaml/.cs            — Hosts DocDesignView with no Word dependency
    └── App.xaml/.cs
```

## Tools Provided

- **Paragraphs** — Apply/remove character style on first word; hanging indent (single/double window); center last line. Style picker filters document styles by usage.
- **Columns** — Align two-column sections by iteratively adjusting paragraph spacing until column heights match. Find next uneven column pair.
- **Spacing** — Fine-tune space-after, space-before, line spacing, word spacing, and character stretch with +/−/reset controls.

## Architecture

Follows MVVM. Each feature area has its own ViewModel inheriting `ViewModelBase` from `WpfLib`. The main `DocDesignViewModel` is a thin coordinator that instantiates the three sub-ViewModels.

`ScreenFreeze` and `UndoRecord` are RAII wrappers (`IDisposable`) used in `using` blocks around any operation that touches the document — preventing screen flicker and grouping changes into a single Ctrl+Z undo record.

`Vsto` is a static gateway set once by the View constructor. All operation classes read from it directly.

## Visual Style

Matches `RegexFindLib` and `WebSitesLib` — same adaptive mid-gray color tokens, same `Segoe UI` font, same icon button pattern (`Viewbox/Canvas/Path`). Section headers use a custom `Expander` template styled as a title bar with a chevron indicator.
