# Helpers — VSTO Add-in Utility Classes

Shared utilities for the KleiKodesh VSTO Word add-in. These handle task pane lifecycle, Word interop, Office theme detection, settings, and UI helpers.

## Files

### Task Pane Management

**`TaskpaneManager.cs`** — Creates/reuses custom task panes. Key methods:
- `Show(UserControl control, string title, double width)` — Shows a task pane; reuses existing pane of same type for the active Word window, else creates new via `CustomTaskPanes.Add()`
- `CloseAll()` — Closes all open task panes (called on shutdown)
- `TriggerUpdateCheck()` — Checks for updates on first pane open
- Dock position is RTL-aware: Hebrew UI docks left, others dock right

**`WpfTaskPane.cs`** — Hosts a WPF UserControl inside a WinForms-based VSTO task pane. Handles:
- WPF/WinForms interop via `ElementHost`
- Theme propagation from Office to WPF
- Keyboard focus management

**`TaskPanePopOut.cs`** — Pops a docked task pane into a floating, resizable window. Uses `HwndSource` to host the WPF content with proper keyboard and focus handling.

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

- **Pane reuse** — `TaskpaneManager` checks `CustomTaskPanes` for an existing pane of the same type before creating a new one. This prevents duplicate panes.
- **Theme propagation** — WPF task panes subscribe to `OfficeThemeWatcher.ThemeChanged` and update their resource dictionaries dynamically.
- **RAII for Word interop** — Operations that modify the Word document use `ScreenFreeze` and `UndoRecord` wrappers (defined in DocDesignLib) to prevent flicker and group undo steps.
