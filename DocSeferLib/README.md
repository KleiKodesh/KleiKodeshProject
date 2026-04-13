# DocSeferLib — Torah Document Formatting Library (עיצוב תורני)

A WPF class library providing formatting tools tailored for Hebrew Torah documents. It is loaded by `KleiKodeshVsto` and displayed in a task pane when the user clicks the **עיצוב תורני** ribbon button.

## How It Integrates with the VSTO Add-in

`KleiKodeshVsto` instantiates `DocSeferLib.DocSeferLibView`, passing the Word `Application` object and the VSTO `Factory`. The view is hosted in a WPF task pane via `WpfTaskPane.Show()`. All document manipulation goes through the Word Interop API.

## Folder Structure

```
DocSeferLib/
├── UI/
│   ├── DocSeferView.xaml / .cs   — Root WPF UserControl (the task pane UI)
│   ├── DocseferViewModel.cs      — Main ViewModel; coordinates sub-ViewModels
│   └── DocSeferDictionary.xaml   — Shared WPF resource dictionary (styles, templates)
├── Columns/
│   ├── AlignColumns.cs           — Aligns table columns across selected paragraphs
│   ├── ColumnsHelper.cs          — Low-level column measurement helpers
│   └── ColumnsViewModel.cs       — ViewModel for the Columns tab
├── Paragraphs/
│   ├── FirstWordStyle.cs         — Applies a named character style to the first word
│   ├── FirstWordHanging.cs       — Sets a hanging indent on the first word
│   ├── CenterLastLine.cs         — Centers the last line of a paragraph
│   ├── PargaraphsBase.cs         — Shared base class for paragraph operations
│   └── ParagraphsViewModel.cs    — ViewModel for the Paragraphs tab
├── Spacing/
│   ├── SpacingHelper.cs          — Calculates and applies line/paragraph spacing
│   └── SpacingViewModel.cs       — ViewModel for the Spacing tab
└── Helpers/
    ├── Vsto.cs                   — VSTO interop utilities (selection, range helpers)
    ├── ScreenFreeze.cs           — Freezes screen updates during bulk operations
    ├── UndoRecord.cs             — Wraps operations in a named Word undo record
    └── RangePageData.cs          — Retrieves page/position data for a Word Range
```

## Tools Provided

- **Columns** — Align and balance columns in Torah-style tabular layouts.
- **Paragraphs** — Style the first word of a paragraph (bold, special style, hanging indent); center the last line.
- **Spacing** — Fine-tune line spacing and paragraph spacing for dense Hebrew text.

## Architecture

Follows MVVM. Each feature area has its own ViewModel that receives the Word `Application` reference and calls the corresponding operation class. `ScreenFreeze` and `UndoRecord` are used as RAII wrappers around any operation that touches the document, ensuring the screen doesn't flicker and the user can undo the change with a single Ctrl+Z.
