# KleiKodeshVsto — VSTO Word Add-in

The core Word add-in. It is installed by `Build/Installer` and loaded automatically by Word on startup.

## What It Does

Adds a custom ribbon tab to Word and opens task panes for each tool:

| Ribbon Button            | Tool                      | Implementation                                 |
| ------------------------ | ------------------------- | ---------------------------------------------- |
| כזית (Kezayit)           | Seforim viewer & search   | `KezayitLib.AppViewer` in WebView2 task pane   |
| עיצוב תורני (KleiKodesh) | Torah document formatting | `DocDesign.DocDesignView` in WPF task pane |
| דרך האתרים (WebSites)    | Curated website browser   | `WebSitesLib2.WebSitesView` in WPF task pane   |
| חיפוש רגקס (RegexFind)   | Regex find & replace      | HTML-based task pane                           |
| הגדרות (Settings)        | Add-in settings           | WPF task pane                                  |
| About                    | About document            | Opens a Word template                          |

## Folder Structure

```
KleiKodeshVsto/
├── ThisAddIn.cs          — Entry point; startup/shutdown hooks
├── Ribbon/
│   ├── KeliKodeshRibbon.cs   — IRibbonExtensibility; dispatches button clicks
│   └── KeliKodeshRibbon.xml  — Ribbon XML layout
├── Common/
│   └── KleiKodeshWebView.cs  — WebView2 wrapper for HTML task panes
├── Helpers/
│   ├── TaskpaneManager.cs    — Creates/reuses custom task panes; triggers update check
│   ├── WpfTaskPane.cs        — Hosts WPF UserControls inside a task pane
│   ├── TaskPanePopOut.cs     — Pop-out task pane into a floating window
│   ├── WdActionManager.cs    — Word document action helpers
│   ├── WordWindowHelper.cs   — Window snapping / child document helpers
│   ├── OfficeThemeWatcher.cs — Detects Office theme changes and notifies task panes
│   ├── SettingsManager.cs    — Registry-backed settings (INI-style sections/keys)
│   ├── MsgBox.cs             — Themed message box wrapper
│   └── JsonExtensions.cs     — JSON serialization helpers
├── KleiKodeshVsto/RegexInWord/                — Self-contained regex search/replace tool (HTML)
└── Resources/                — Embedded resource files
```

## How It Integrates with Word

- Implements `IRibbonExtensibility` to inject the custom ribbon tab.
- Uses `Globals.ThisAddIn.CustomTaskPanes` to create docked task panes.
- Accesses the active document via `Globals.ThisAddIn.Application` (Word Interop).
- Task pane dock position is RTL-aware: Hebrew UI docks left, others dock right.
- `OfficeThemeWatcher` polls the Office theme and propagates changes to hosted controls.
- On shutdown, cancels any pending Word-to-PDF conversions and runs any deferred installer update.

## Task Pane Lifecycle

`TaskpaneManager.Show(userControl, title, width)` checks if a pane of the same type already exists for the active Word window. If so it reuses it; otherwise it creates a new one. This prevents duplicate panes when the user clicks the ribbon button again.
