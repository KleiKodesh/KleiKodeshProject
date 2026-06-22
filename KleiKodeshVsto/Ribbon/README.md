# Ribbon — Word Ribbon UI Definition

Custom ribbon tab injected into Microsoft Word. Defines buttons, their icons, tooltips, and click handlers.

## Files

**`KeliKodeshRibbon.cs`** — Implements `IRibbonExtensibility`. Controls ribbon lifecycle:
- `GetCustomUI(string ribbonID)` — Returns ribbon XML on load
- `OnButtonClick(IRibbonControl control)` — Central click dispatcher, routes by button tag
- `GetImage(IRibbonControl control)` — Returns ribbon button icons from embedded resources
- `GetLabel(IRibbonControl control)` — Returns Hebrew button labels

**`KeliKodeshRibbon.xml`** — Ribbon XML layout defining:
- Ribbon tab "כלי קודש" with button groups
- Each button: id, tag (maps to `OnButtonClick`), label, image, screentip/supertip
- Buttons are conditionally visible based on settings (to allow component enable/disable)

**`RibbonSettingsControl.cs`** — Settings controller:
- Loads/saves ribbon component visibility from `SettingsManager`
- Toggles which ribbon buttons are shown

**`RibbonSettingsView.xaml` / `RibbonSettingsView.xaml.cs`** — WPF settings UI:
- Checkbox list for each ribbon component
- "Default button" selector
- "Check for updates" toggle
- All text in Hebrew

## Ribbon Buttons

| Tag | Label | Tool | Implementation |
|-----|-------|------|----------------|
| `KitveiHakodesh` | כתבי הקודש | Seforim viewer | `KitveiHakodeshLib.AppViewer` in WebView2 task pane |
| `RegexFind` | חיפוש רגקס | Regex find & replace | `RegexFindLib.UI.RegexFindView` in WPF task pane |
| `DocDesign` | עיצוב תורני | Torah formatting | `DocDesignLib.DocDesignView` in WPF task pane |
| `WebSites` | דרך האתרים | Website browser | `WebSitesLib.WebSitesView` in WPF task pane |
| `Kiwix` | קורא קיוויקס | ZIM file reader | `KiwixLib.KiwixWebview` in WinForms task pane |
| `Settings` | הגדרות | Add-in settings | `RibbonSettingsView` dialog |

## How to Add a New Ribbon Button

1. Add the button XML in `KeliKodeshRibbon.xml`
2. Add the tag mapping in `OnButtonClick` in `KeliKodeshRibbon.cs`
3. Add the image to `Resources/` folder
4. Add visibility toggle in `RibbonSettingsControl.cs` if the component should be optional
5. Add checkbox entry in `RibbonSettingsView.xaml`
6. Wire the task pane creation in `TaskpaneManager.Show()`

## Conditional Visibility

Ribbon buttons respect user settings stored in registry:
- Each component (except Settings) can be hidden via `SettingsManager`
- Settings button is always visible
- Visibility is checked in `GetVisible(IRibbonControl control)` callback in `KeliKodeshRibbon.cs`
