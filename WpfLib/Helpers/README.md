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

### `FontsProvider.cs`
The solution's single font source. Enumerates system font families through DirectWrite and
tests each one for a real א glyph by parsing the face's own `cmap` table. Replaced WPF's
`Fonts.SystemFontFamilies` and `System.Drawing`'s `InstalledFontCollection`, both of which are
process-lifetime snapshots that never see fonts installed while the app runs. Twin file of the
service's `HebrewFontsProvider.cs` (native-AOT leg) — keep the two in sync.

Key methods:
- `GetFontFamilies()` — Every family as `FontFamilyInfo` (name + `HasHebrew`), Hebrew families
  first and alphabetical within each group
- `GetHebrewFonts()` — Just the Hebrew-capable family names, alphabetical
- `HasHebrew(string)` — Tests one family by name, without enumerating them all

Stateless by design: every call re-scans, so fonts installed mid-session show up. Costs roughly
a second — call it off the UI thread and show a loading row.

### `FontsHelper.cs`
WPF projection of `FontsProvider` for font pickers.

Key methods:
- `GetFontsCollection()` — Every family as a WPF `FontFamily`, Hebrew ones first
- `HasHebCharacters(this FontFamily)` — Extension method; true when the family has an א glyph

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
