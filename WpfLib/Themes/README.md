# WpfLib Shared Style Palette

## Overview

`WpfLib/Themes/` contains a unified, reusable style palette for all KleiKodesh task pane libraries (RegexFindLib, DocDesignLib, WebSitesLib). This eliminates duplication of core UI patterns while preserving library-specific customizations.

**Single entry point:** `OfficePalette.xaml` merges all shared styles. Consuming libraries import this one file instead of maintaining duplicate copies.

---

## What's Included (Shared Across All 3 Task Pane Libs)

### 1. **Brushes.xaml** — Adaptive Color Tokens

10 brush resources that work on any Office theme (light, dark, black):

| Key | Value | Purpose |
|-----|-------|---------|
| `BgSecBrush` | `#0F808080` | Secondary background (6% mid-gray overlay) |
| `BgTerBrush` | `#1A808080` | Tertiary background (10% overlay) |
| `HoverBrush` | `#0A808080` | Hover state (4% overlay) |
| `PressedBrush` | `#14808080` | Pressed state (8% overlay) |
| `BorderBrush` | `#50808080` | Standard border (31% overlay) |
| `BorderStrong` | `#80808080` | Strong border (50% overlay) |
| `AccentBrush` | `#0078D4` | Office accent blue |
| `AccentHover` | `#106EBE` | Accent hover state |
| `AccentPressed` | `#005A9E` | Accent pressed state |
| `SelectedBrush` | `#3300B4FF` | Selected item highlight (20% accent tint) |
| `TextSecBrush` | `#99808080` | Secondary text (60% opacity) |

**Why mid-gray overlays?** Mid-gray (#808080) is equidistant from black and white, so it's visible on both light and dark backgrounds. This single palette adapts automatically to any Office theme without hardcoding light/dark variants.

**Status:** Byte-for-byte identical in RegexFindLib, DocDesignLib, and WebSitesLib. Consolidated here to eliminate duplication.

---

### 2. **ScrollBarStyles.xaml** — Thin Edge-Style Scrollbar

Implicit `ScrollBar` style + templates for vertical and horizontal scrollbars:

- **Width/Height:** 12px track, no arrows
- **Thumb:** Rounded, 20px minimum, mid-gray with hover/drag states
- **Appearance:** Matches modern browsers (Edge, Chrome)

**Status:** Identical copy-paste across all 3 libs. Now maintained in one place.

---

### 3. **ComboBoxStyles.xaml** — Office ComboBox

Implicit `ComboBox` style + `OfficeComboItem` container style:

- **Appearance:** 28px height, 4px corner radius, adaptive bg/fg from ancestor UserControl
- **Behavior:** Virtualized dropdown, RTL-aware, placeholder support for editable mode
- **Item styling:** Hover/selected/highlighted states with mid-gray overlays

**Status:** DocDesignLib literally comments "Identical to RegexFindLib". WebSitesLib is the same minus placeholder. Consolidated here.

---

### 4. **ButtonStyles.xaml** — Button & Toggle Styles

Four keyed styles for common button patterns:

| Style | Type | Use Case |
|-------|------|----------|
| Implicit `Button` | Button | Flat icon buttons (toolbar, inline) |
| Implicit `ToggleButton` | ToggleButton | Toggle without checked highlight |
| `CheckedToggle` | ToggleButton | Toggle with blue selected state (toolbar toggles) |
| `ActionButton` | Button | Bordered action buttons (OK, Apply, Cancel in dialogs) |

All use mid-gray overlays for hover/pressed states. Disabled state at 35% opacity.

**Status:** DocDesignLib and WebSitesLib are identical. RegexFindLib splits these into named variants (`InputIconButton`, `TitleToggle`, `FormatButton`) — those remain library-specific.

---

### 5. **CheckBoxStyles.xaml** — VSCode-Style Checkbox

Implicit `CheckBox` style:

- **Appearance:** 14×14 square box, 2px corner radius, 1.5px stroke checkmark
- **States:** Unchecked (transparent), checked (light tint + darker border), hover (darker border)
- **Foreground:** Inherits from ancestor FrameworkElement

**Status:** Identical in RegexFindLib and WebSitesLib. DocDesignLib has a simpler version without the custom template — this is the full-featured version.

---

## What's NOT Included (Library-Specific Styles)

### RegexFindLib — Kept Local

These styles are regex-find-specific and don't belong in a shared palette:

| File | Styles | Reason |
|------|--------|--------|
| `ButtonStyles.xaml` | `InputIconButton`, `InputIconToggle`, `TitleToggle`, `IconToggle`, `FormatButton` | Named variants for specific regex UI patterns (format toolbar, palette toggles) |
| `FormatToggle.xaml` | `FormatToggle` | Three-state checkbox (null/true/false) with red diagonal line for "excluded" state — regex-specific |
| `ColorPickerStyles.xaml` | `ColorPickerButton` + `SwatchTemplate` | Custom color picker control with theme/standard color swatches — regex-specific |
| `PaletteStyles.xaml` | `RegexTipTemplate`, `RegexPalettePanel` | Regex tip display (symbol | meaning + example) — regex-specific |
| `SpinnerTextBoxStyles.xaml` | `SpinnerTextBox` | Numeric spinner with up/down buttons — regex-specific |
| `FormatOptionsRowStyles.xaml` | `FormatOptionsRow` | Format toolbar layout (B/I/U/x²/x₂/A/eraser/pencil) — regex-specific |

**Decision:** These are tightly coupled to regex find & replace UI. Extracting them would require extracting the entire regex domain model. Not worth the abstraction cost.

---

### DocDesignLib — Kept Local

| File | Styles | Reason |
|------|--------|--------|
| `ExpanderStyles.xaml` | `ExpanderToggleTemplate`, implicit `Expander` | Torah document formatting (columns, paragraphs, spacing) uses expanders for section headers. Not a general UI pattern. |
| `ButtonStyles.xaml` | `ResetButton`, `IncreaseButton`, `DecreaseButton` | Domain-specific buttons for paragraph/column controls (reset indent, increase/decrease spacing). Hebrew tooltips ("איפוס", "הגדל", "הקטן") are hardcoded. |
| `MiscStyles.xaml` | `ResultItem`, `InlineTextBox`, `SearchTextBox`, `InputWrapper` | Document formatting-specific input patterns. Not reusable across other task panes. |

**Decision:** These are deeply tied to the Torah document formatting domain. Extracting them would require extracting domain concepts (paragraphs, columns, spacing). Not worth the abstraction cost.

---

### WebSitesLib — Kept Local

| File | Styles | Reason |
|------|--------|--------|
| `AddressBarStyles.xaml` | `AddressBarCombo`, `DropdownBorder`, `TabTitle`, `TabTitleActive`, `TabClosePath` | Browser-specific UI (address bar, tab strip, tab close button). Not reusable. |
| `MiscStyles.xaml` | `TabItemStyle`, `DialogListItem` | Browser-specific list item styles. Not reusable. |

**Decision:** These are browser UI patterns specific to the website viewer. No other task pane needs them.

---

### Build/Installer — Kept Local

| File | Styles | Reason |
|------|--------|--------|
| `InstallerStyles.xaml` | `PrimaryButton`, `SecondaryButton`, `GhostButton`, `AccentProgress` | Light-mode palette (#7333FF purple accent, #FFFFFF bg, #1F1F1F text). Completely different design language from Office theme. Not compatible with task pane libraries. |
| `App.xaml` | `SlimScrollThumb`, implicit `ScrollBar` | Installer-specific scrollbar (6px visible, 8px track). Different from task pane scrollbar (12px). |

**Decision:** The installer is a standalone WPF app with its own design system. Sharing styles would require conditional logic or multiple palettes. Not worth the complexity.

---

## Usage

### For Consuming Libraries

Replace local duplicates with a single import:

**Before (RegexFindLib/RegexFindDictionary.xaml):**
```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="/RegexFindLib;component/ui/themes/brushes.xaml"/>
    <ResourceDictionary Source="/RegexFindLib;component/ui/themes/comboboxstyles.xaml"/>
    <ResourceDictionary Source="/RegexFindLib;component/ui/themes/scrollbarstyles.xaml"/>
    <!-- ... other local styles ... -->
</ResourceDictionary.MergedDictionaries>
```

**After:**
```xml
<ResourceDictionary.MergedDictionaries>
    <!-- Shared palette from WpfLib -->
    <ResourceDictionary Source="/WpfLib;component/themes/officepalette.xaml"/>
    <!-- Library-specific styles -->
    <ResourceDictionary Source="/RegexFindLib;component/ui/themes/buttonstyles.xaml"/>
    <ResourceDictionary Source="/RegexFindLib;component/ui/themes/formattoggle.xaml"/>
    <!-- ... other local styles ... -->
</ResourceDictionary.MergedDictionaries>
```

### For New Libraries

If adding a new task pane library to KleiKodesh:

1. Create a root dictionary (e.g., `MyLibDictionary.xaml`)
2. Merge `OfficePalette.xaml` first
3. Add only library-specific styles after

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <!-- Shared palette -->
        <ResourceDictionary Source="/WpfLib;component/themes/officepalette.xaml"/>
        <!-- Library-specific -->
        <ResourceDictionary Source="/MyLib;component/ui/themes/mydomainstyles.xaml"/>
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

---

## Design Principles

### 1. **Adaptive, Not Hardcoded**

All colors use mid-gray overlays (#808080 at various opacities) instead of hardcoded light/dark variants. This single palette works on any Office theme automatically.

### 2. **VSTO-Safe**

All values are inlined in control templates. No `{StaticResource}` inside `<ControlTemplate>` bodies. This prevents `XamlParseException` when templates are instantiated in separate `HwndSource` windows (Popup, ContextMenu, separate dialogs).

### 3. **Domain-Agnostic**

Only styles that are truly generic (scrollbar, combobox, button, checkbox) are shared. Domain-specific patterns (regex toggles, document formatting controls, browser tabs) stay in their libraries.

### 4. **Minimal Abstraction**

No unnecessary base styles or inheritance chains. Each style is self-contained and can be copied to a library if needed without breaking dependencies.

---

## Future Consolidation

As the project evolves, watch for these patterns that could move to WpfLib:

- **TextBox styles** — If multiple libs define similar input patterns (search box, inline edit)
- **ListBox/ListBoxItem** — If multiple libs use similar list item hover/selected states
- **Separator/Divider** — If multiple libs define similar line separators
- **ProgressBar** — If multiple libs need progress indicators

**Rule:** Only consolidate if the style appears in 2+ libraries with minimal variation. One-off styles stay local.

---

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `Brushes.xaml` | ~30 | 10 adaptive color tokens |
| `ScrollBarStyles.xaml` | ~60 | Thin Edge-style scrollbar |
| `ComboBoxStyles.xaml` | ~120 | Office ComboBox + item |
| `ButtonStyles.xaml` | ~110 | Button, ToggleButton, CheckedToggle, ActionButton |
| `CheckBoxStyles.xaml` | ~50 | VSCode-style checkbox |
| `OfficePalette.xaml` | ~20 | Root entry point (merges all 5) |

**Total:** ~390 lines of shared, reusable XAML.

---

## See Also

- [WPF Best Practices](../../.kiro/steering/wpf/wpf-best-practices.md) — Coding conventions, custom controls, MVVM
- [ElementHost/VSTO](../../.kiro/steering/wpf/05-elementhost-vsto.md) — Why StaticResource inside ControlTemplates crashes
- RegexFindLib/README.md — Regex-specific styles
- DocDesignLib/README.md — Torah formatting-specific styles
- WebSitesLib/README.md — Browser-specific styles
