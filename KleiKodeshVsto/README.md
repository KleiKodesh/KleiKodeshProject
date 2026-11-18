# KleiKodeshVsto — VSTO Word Add-in

The core Word add-in. It is installed by `Build/Installer` and loaded automatically by Word on startup.

## What It Does

Adds a custom ribbon tab to Word and opens task panes for each tool:

| Ribbon Button            | Tool                      | Implementation                                 |
| ------------------------ | ------------------------- | ---------------------------------------------- |
| כתבי הקודש (KitveiHakodesh)           | Seforim viewer & search   | `KitveiHakodeshLib.AppViewer` in WebView2 task pane   |
| עיצוב תורני (KleiKodesh) | Torah document formatting | `DocDesign.DocDesignView` in WPF task pane     |
| דרך האתרים (WebSites)    | Curated website browser   | `WebSitesLib.WebSitesView` in WPF task pane    |
| חיפוש רגקס (RegexFind)   | Regex find & replace      | `RegexFindLib.UI.RegexFindView` in WPF task pane       |
| הגדרות (Settings)        | Add-in settings           | WPF task pane                                  |
| About                    | About page                | Opens the About page in the browser            |

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
│   ├── TaskpaneManager.cs    — The add-in's side panes (plugs the add-in into OfficePaneChrome)
│   ├── WpfTaskPane.cs        — Shows WPF views as side-pane views
│   ├── OfficePaneChrome/     — Reusable: shared side panes, title-strip selector, pop-out window
│   ├── WdActionManager.cs    — Word document action helpers
│   ├── WordWindowHelper.cs   — Window snapping / child document helpers
│   ├── OfficeThemeWatcher.cs — Detects Office theme changes and notifies task panes
│   ├── SettingsManager.cs    — Registry-backed settings (INI-style sections/keys)
│   ├── MsgBox.cs             — Themed message box wrapper
│   └── JsonExtensions.cs     — JSON serialization helpers
├── DocDesign/                — Torah document formatting (WPF, see DocDesign/README.md)
├── RegexInWord/              — Regex find & replace (WPF, see RegexFindLib/README.md)
│   ├── RegexFindLib/         — WPF library (UserControl + MVVM)
│   └── RegexFindDemo/        — Standalone WPF demo app (no Word required)
├── WebSitesLib/              — Curated website browser (WPF + WebView2, see WebSitesLib/README.md)
│   ├── WebSitesLib/          — WPF library
│   └── WebSitesDemo/         — Standalone WPF demo app
├── Kiwix/                    — ZIM file reader (WinForms + kiwix-js, see KIWIX_CHANGES.md)
│   ├── KiwixLib/             — WinForms UserControl host
│   ├── KiwixDemoApp/         — Standalone WinForms demo app
│   └── kiwix-js-main/        — Upstream kiwix-js source (customised for Hebrew/RTL)
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

Each document window has one side pane (a custom task pane) that hosts views: `TaskpaneManager.Show(id, factory, title)` brings view `id` (its ribbon button id) into the current side pane, building it only if that side pane doesn't have it yet; views moved out stay alive with their state. The side pane's title strip shows a selector (current view's icon and name) whose menu lists the ribbon buttons tagged `tag="pane"`, plus "open new side pane", and a pop-out button that moves the side pane into its own window. Office's X closes the side pane and disposes its views. See `Helpers/OfficePaneChrome/SidePanes.cs`.
