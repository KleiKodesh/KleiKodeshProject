# Ribbon

Word ribbon UI definition and event handlers.

## Files

- `KeliKodeshRibbon.cs` — Ribbon UI implementation (IRibbonExtensibility)
- `KeliKodeshRibbon.xml` — Ribbon XML definition with buttons and controls
- `RibbonSettingsControl.cs` — Settings control for ribbon
- `RibbonSettingsView.xaml` / `RibbonSettingsView.xaml.cs` — WPF settings UI

## Ribbon Buttons

The ribbon defines buttons for:
- **כתבי הקודש** — KitveiHakodesh seforim viewer
- **חיפוש רגקס** — Regex find & replace
- **עיצוב תורני** — Torah document formatting
- **דרך האתרים** — Website browser task pane
- **קורא קיוויקס** — ZIM file reader (Kiwix)
- **הגדרות** — Settings

Each button maps to a task pane or dialog that gets displayed when clicked.
