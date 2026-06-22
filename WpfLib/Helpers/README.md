# Helpers — Shared Utility Classes

Utility classes shared across all WPF task pane libraries.

## Files

### `HebrewNumbering.cs`
Converts integers (1–9999) to Hebrew numeral strings (e.g. 5779 → ה׳תשע״ט). Used by DocDesignLib for Hebrew page numbering in Torah documents.

Key methods:
- `NumberToHebrewString(int)` — Standard Hebrew numeral
- `NumberToHebrewStringWithGeresh(int)` — Adds ״/׳ quotation marks
- `IsHebrewNumber(string)` — Checks if string is a valid Hebrew numeral

### `HebrewDateHelper.cs`
Hebrew calendar date utilities.

Key methods:
- `GetTodayHebrewDate()` — Returns current date as Hebrew date string
- `GetParsha()` — Returns weekly Torah portion (if applicable)
- `IsHoliday(DateTime)` — Checks if date is a Jewish holiday
- `GetOmerDay()` — Returns current day of Omer count (0 if not in Omer period)

### `FontsHelper.cs`
System font enumeration and management.

Key methods:
- `GetInstalledFonts()` — Returns list of all installed font family names
- `FontExists(string)` — Checks if a font is installed
- `GetHebrewFonts()` — Filters to fonts with Hebrew character support

### `MsgBox.cs`
Themed message box wrapper. Respects Office theme (light/dark) for consistent appearance.

Key methods:
- `Show(string text, string title, MsgBoxButton buttons)` — Shows themed dialog
- `ShowError(string text)` — Error dialog with red accent
- `ShowWarning(string text)` — Warning dialog with yellow accent
- Returns `MsgBoxResult` (Yes/No/Cancel/OK)

### `ObservableCollectionExtensions.cs`
Extension methods for `ObservableCollection<T>`:
- `AddRange(IEnumerable<T>)` — Bulk-add items with single CollectionChanged event
- `RemoveAll(Predicate<T>)` — Bulk-remove matching items
- `ReplaceWith(IEnumerable<T>)` — Clear + AddRange in one operation

Use instead of loop-adding to avoid per-item UI updates.

### `EventArgs.cs`
Generic event args: `EventArgs<T>` with `Value` property. Use for strongly-typed events without creating custom EventArgs subclasses.

### `DependencyHelper.cs`
Simple service locator for resolving dependencies in VSTO context where DI containers are not available.

Key methods:
- `Resolve<T>()` — Resolves registered service
- `Register<T>(T instance)` — Registers singleton instance
- `RegisterLazy<T>(Func<T> factory)` — Registers lazy factory

### `ConfigurationManagerWrapper.cs`
Wrapper around `System.Configuration.ConfigurationManager` for reading app.config settings. Provides typed access with fallback defaults.

Key methods:
- `GetSetting(string key, string defaultValue)` — Reads app setting string
- `GetSetting<T>(string key, T defaultValue)` — Reads and converts to type T
