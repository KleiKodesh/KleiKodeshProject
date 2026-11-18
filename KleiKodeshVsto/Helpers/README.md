# Helpers — VSTO Add-in Utility Classes

Shared utilities for the KleiKodesh VSTO Word add-in. These handle task pane lifecycle, Word interop, Office theme detection, settings, and UI helpers.

## Files

### Task Pane Management

**`TaskpaneManager.cs`** — The add-in's side panes, on top of `OfficePaneChrome.SidePanes`. Key methods:
- `Show(id, factory, title)` — Shows view `id` (ribbon button id) in the current side pane of the active window; builds it only when that side pane doesn't have it
- `OpenNewPane()` — Another side pane in the active window (ribbon "open new side pane")
- `FindAll<T>(id)` — Live views of an id, without creating one
- `ToggleFocus()` — Macro shortcut: keyboard focus between document and current view
- Plugs in the add-in's specifics: dock position (RTL-aware default), the side pane's width (one for all views, 510 by default), pop-out bounds, Office theme, flushing views before disposal, popup owners, closing with the document

**`WpfTaskPane.cs`** — Shows WPF views as side-pane views (wrapped in an `ElementHost`, following the Office theme colours).

**`OfficePaneChrome/`** — Self-contained and reusable (no add-in types): `SidePanes` (shared side panes, view shuffling, pop-out, X closes), `OfficeTitleStrip` (selector and pop-out button drawn into Office's title strip), `PaneMenu`, `RibbonPanes` (pane list from the ribbon's `tag="pane"` buttons and its own callbacks), `ChromeWindow` (custom-title-bar window ported from FluentChromeTabs), `PopOutWindow`.

### Word Integration

**`WdActionManager.cs`** — Manages Word document actions:
- `InsertText(string)` — Inserts text at cursor with formatting
- `ApplyStyle(string)` — Applies a Word style to selection
- `GetSelectionText()` — Reads selected text
- `SetSelectionText(string)` — Replaces selected text
- `RunWordCommand(string command)` — Executes a Word VBA command

**`WordWindowHelper.cs`** — Window management utilities for Word:
- `GetWordWindowHandle()` — Returns HWND of the main Word window
- `SnapToWordWindow(Window wpfWindow)` — Snaps a floating window to Word's position
- `CenterOnWordWindow(Window wpfWindow)` — Centers dialog on Word

### Settings & Theme

**`SettingsManager.cs`** — Registry-backed settings with INI-style sections/keys:
- `GetSetting(string section, string key, T defaultValue)` — Reads typed setting from `HKCU\SOFTWARE\KleiKodesh\Settings`
- `SetSetting<T>(string section, string key, T value)` — Writes setting
- `DeleteSetting(string section, string key)` — Removes a setting
- Used for ribbon visibility, window positions, and user preferences

**`OfficeThemeWatcher.cs`** — Polls the Office theme registry key and notifies subscribers:
- `ThemeChanged` event — Fires when Office switches light/dark/black theme
- `CurrentTheme` property — Returns `OfficeTheme` enum (Light, Dark, Black)
- All WPF task panes subscribe to propagate theme changes to their UI

### UI Helpers

**`MsgBox.cs`** — Themed message box for VSTO context:
- `Show(string text, string title, MsgBoxButton buttons)` — Office-theme-aware dialog
- `ShowError(string text)` — Error with Hebrew button text
- Returns `MsgBoxResult` enum

**`FormSettings.cs`** — Settings form UI for the add-in. Loaded from the ribbon Settings button. Manages:
- Ribbon component visibility toggles
- Default button preferences
- Auto-update enable/disable

**`JsonExtensions.cs`** — JSON serialization helpers:
- `ToJson<T>(this T obj)` — Serializes to JSON string
- `FromJson<T>(this string json)` — Deserializes from JSON string
- Wraps `Newtonsoft.Json` with error handling

## Key Patterns

- **One side pane, many views** — ribbon buttons shuffle views into the current side pane instead of opening a pane each; a second side pane is opened on purpose ("open new side pane").
- **Theme propagation** — WPF task panes subscribe to `OfficeThemeWatcher.ThemeChanged` and update their resource dictionaries dynamically.
- **RAII for Word interop** — Operations that modify the Word document use `ScreenFreeze` and `UndoRecord` wrappers (defined in DocDesignLib) to prevent flicker and group undo steps.
