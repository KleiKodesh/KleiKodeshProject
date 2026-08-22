# WpfLib Shared Style Palette

## Overview

`WpfLib/Themes/` is the one place the project's WPF look is defined. Every task
pane library (RegexFindLib, DocDesignLib, WebSitesLib, Nakdan) and the ribbon
settings pane draws from it.

**Single entry point:** `OfficePalette.xaml` merges all shared styles. Consuming
libraries merge that one file and then add only what is genuinely theirs.

---

## What's Shared

### 1. `Brushes.xaml` — Adaptive Color Tokens

11 brush resources that work on any Office theme (light, dark, black):

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

**Why mid-gray overlays?** Mid-gray (`#808080`) is equidistant from black and
white, so it is visible on both light and dark backgrounds. One palette adapts to
any Office theme without light/dark variants.

### 2. `ScrollBarStyles.xaml` — Thin Edge-Style Scrollbar

Implicit `ScrollBar` plus the `ScrollThumb`, `VertScrollBar` and `HorzScrollBar`
templates. 12px track, no arrows, 20px minimum thumb.

Note that `Width`/`MinWidth` are set **inside the Vertical trigger**, not on the
base style. Setting them on the base clamps a horizontal scrollbar to 12px wide —
that was a real bug in one of the copies this file replaced.

### 3. `ComboBoxStyles.xaml` — Office ComboBox

Implicit `ComboBox` plus the `OfficeComboItem` container style. 28px height,
adaptive bg/fg from the ancestor `UserControl`, virtualized RTL-aware dropdown.

Set `Tag` to a prompt string to get placeholder text on an editable ComboBox;
leave `Tag` unset and the placeholder stays collapsed.

### 4. `ButtonStyles.xaml` — Buttons & Toggles

| Style | Type | Use case |
|-------|------|----------|
| `IconButton` | Button | Flat icon button. Also the implicit `Button`. |
| `IconToggle` | ToggleButton | Flat toggle, **no** checked highlight |
| `AccentToggle` | ToggleButton | Flat toggle, accent fill while checked |
| `CheckedToggle` | ToggleButton | Bordered toggle, accent fill while checked |
| `ActionButton` | Button | Bordered dialog button (OK, Apply, Cancel) |
| `IconPath` | Path | Vector icon inside a button; inherits pane foreground |

Hover/pressed use mid-gray overlays; disabled is 35% opacity. `IconButton` and
`ActionButton` bind `TextBlock.Foreground` on the content presenter so button
*text* follows the Office theme, not just the icon.

**`AccentToggle` uses `MultiTrigger`s, deliberately.** A plain `IsChecked`
trigger loses to `IsMouseOver`, so hovering a checked toggle drops the accent and
an ON control reads as OFF. The MultiTriggers keep it and brighten it instead.

**There is no implicit `ToggleButton`.** Both toggle behaviours are legitimate —
DocDesignLib wants no checked highlight, WebSitesLib wants one — so each library
picks with a single line of its own:

```xml
<Style TargetType="ToggleButton" BasedOn="{StaticResource AccentToggle}"/>
```

**Sizing is not set here** beyond `Padding`. A library needing a fixed metric
derives with `BasedOn` and adds `Width`/`Height`/`Margin`, so this file stays
about how a control *looks*, not how big it is.

### 5. `CheckBoxStyles.xaml` — VSCode-Style Checkbox

Implicit `CheckBox`: 14×14 square box, 2px corner radius, 1.5px vector tick.

---

## What Stays Local

Only genuinely generic styles are shared. These are tied to their domain and stay
where they are:

| Library | Files | Why |
|---------|-------|-----|
| **RegexFindLib** | `FormatToggle`, `ColorPickerStyles`, `PaletteStyles`, `SpinnerTextBoxStyles`, `FormatOptionsRowStyles`, `MiscStyles` (`SearchTextBox`, `InputWrapper`, `InlineTextBox`, `ResultItem`) | Find-and-replace UI. Extracting them would mean extracting the regex domain model. |
| **DocDesignLib** | `ExpanderStyles`, `ButtonStyles` (`ResetButton`, `IncreaseButton`, `DecreaseButton`), `MiscStyles` (`DeleteOverlay`) | Document formatting controls, with hardcoded Hebrew tooltips and icon geometry. |
| **WebSitesLib** | `AddressBarStyles`, `MiscStyles` (`TabItemStyle`, `DialogListItem`, `SeparatorLine`) | Browser chrome — address bar, tab strip. |
| **Nakdan** | `OpacityConverter`, `SectionLabel` | A converter and one label style. |
| **Ribbon** | implicit `Button`, `CheckBox`, `RadioButton`, `Card`, `GroupHeader` | Cards, hairline borders and text buttons are a different surface from the icon toolbars, not a drifted copy of them. |
| **Build/Installer** | `InstallerStyles.xaml`, `App.xaml` (`SlimScrollThumb`) | A standalone app with its own design language (purple accent, light-only, 6px scrollbar). |

Each library's `Icons.xaml` also stays local — the icon sets are fully disjoint.

---

## Usage

Merge the palette first, then your own styles:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="/WpfLib;component/themes/officepalette.xaml"/>
        <ResourceDictionary Source="/MyLib;component/ui/themes/mydomainstyles.xaml"/>
    </ResourceDictionary.MergedDictionaries>

    <!-- pick the toggle behaviour this pane wants -->
    <Style TargetType="ToggleButton" BasedOn="{StaticResource IconToggle}"/>
</ResourceDictionary>
```

Pack URI paths must be **all-lowercase** — the WPF BAML compiler lowercases them.

### Where a `BasedOn` may point

A `StaticResource` inside a merged dictionary resolves against that dictionary
and its own merged dictionaries — **not** reliably against a sibling merged into
the same parent. So:

- A **root** dictionary may `BasedOn` a key from anything it merges. Safe.
- A **sub**-dictionary that needs a WpfLib key must merge WpfLib itself:
  ```xml
  <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="/WpfLib;component/themes/buttonstyles.xaml"/>
  </ResourceDictionary.MergedDictionaries>
  ```
  DocDesignLib's and RegexFindLib's `ButtonStyles.xaml` both do this.

---

## Design Principles

1. **Adaptive, not hardcoded.** Mid-gray overlays instead of light/dark variants.
2. **VSTO-safe.** All values inlined in control templates — no `{StaticResource}`
   inside a `<ControlTemplate>` body. That throws `XamlParseException` when a
   template is instantiated in a separate `HwndSource` (Popup, ContextMenu,
   separate dialog).
3. **Look here, size there.** WpfLib owns appearance; libraries own metrics.
4. **A name means one thing.** RegexFindLib's small accent toggle is
   `PaletteToggle`, not `IconToggle`, because `IconToggle` here is the variant
   with no checked highlight. The same name meaning opposite things in two panes
   is a trap.

---

## Future Consolidation

Watch for these moving up if they appear in 2+ libraries with little variation:

- **TextBox styles** — search box, inline edit
- **ListBox / ListBoxItem** — shared hover/selected states
- **Separator / divider** lines
- **ProgressBar**

**Rule:** consolidate only at 2+ libraries with minimal variation. One-offs stay
local.

---

## See Also

- [WPF Best Practices](../../.kiro/steering/wpf/wpf-best-practices.md)
- [ElementHost/VSTO](../../.kiro/steering/wpf/05-elementhost-vsto.md) — why
  `StaticResource` inside a `ControlTemplate` crashes
