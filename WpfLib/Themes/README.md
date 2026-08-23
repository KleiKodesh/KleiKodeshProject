# WpfLib Shared Style Palette

## Overview

`WpfLib/Themes/` is the one place the project's WPF look is defined. Every task
pane library (RegexFindLib, DocDesignLib, WebSitesLib, Nakdan) and the ribbon
settings pane draws from it.

**Pick your layer.** The palette is separable, so depending on it costs only
what you use. Merging a layer never drags in the one above it.

| Layer | File | What it gives you | What it changes |
|-------|------|-------------------|-----------------|
| 1 | `Tokens.xaml` | Colour and type *names* only | **Nothing.** No styles at all, so it cannot change how a control looks |
| 2 | `<Control>Styles.xaml` | Keyed styles for one control | Nothing until you apply them by key |
| 3 | `Defaults.xaml` | Makes layer 2 implicit | Restyles `Button`, `ComboBox`, `CheckBox`, `ScrollBar` |
| 4 | `OfficePalette.xaml` | All of the above | Everything in layer 3 |

`WpfLib` targets **`net48;net10.0-windows`**, so a WPF app on modern .NET can
consume the palette. The five old-style Office projects reference it unchanged.

A task pane that wants the whole suite look merges `OfficePalette.xaml` and is
done. A dialog that wants the suite colours and one bordered button merges
`tokens.xaml` and `buttonstyles.xaml`, uses `{StaticResource ActionButton}`, and
has every other control in it left completely alone.

**Do not merge `OfficePalette.xaml` just to reach a token or a single style** —
it applies a look to four control types.

The control dictionaries declare **keyed styles only**. That is the whole point
of the split: merging `ButtonStyles.xaml` to get `ActionButton` must not silently
restyle every `Button` in the consuming app. Nothing in WpfLib imposes a look
until `Defaults.xaml` is merged, or until you alias a style yourself:

```xml
<Style TargetType="Button" BasedOn="{StaticResource IconButton}"/>
```

The one exception is `UpDownTextBoxStyles.xaml`, which stays implicit. Those are
WpfLib's own controls, so that is their default look rather than a look imposed
on somebody else's control.

---

## Referencing WpfLib from XAML

The library ships an `XmlnsDefinition`, so one declaration covers controls,
converters, attached properties and view models:

```xml
xmlns:kk="http://schemas.kleikodesh.org/wpf"

<kk:UpDownTextBox Value="{Binding Size}"/>
<kk:BoolToVisibilityConverter x:Key="BoolToVis"/>
```

The older `clr-namespace:WpfLib.Controls;assembly=WpfLib` form still works.

---

## What's Shared

### 0. `Typography.xaml` — Type Tokens

| Key | Value | Purpose |
|-----|-------|---------|
| `UiFontFamily` | Segoe UI | The UI chrome font |
| `FontSizeSmall` | 11 | Secondary labels, section headers |
| `FontSizeNormal` | 12 | The default for controls |
| `FontSizeLarge` | 13 | Text inputs, anything read at length |

Sizes are named for their role, not their value, so the scale can be retuned in
one edit. This is the UI chrome font and has nothing to do with the Hebrew
document fonts `WpfLib.Helpers.FontsProvider` enumerates.

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
| `TextSecBrush` | `#99808080` | Secondary text (60% opacity), and the combo placeholder |
| `SelectedHoverBrush` | `#4400B4FF` | Selected **and** hovered, so a selected row keeps reading as selected |
| `ScrollThumbBrush` | `#4D808080` | Scrollbar thumb at rest |
| `ScrollThumbHoverBrush` | `#66808080` | Thumb hovered |
| `ScrollThumbDragBrush` | `#80808080` | Thumb being dragged |

The thumb has its own ramp, heavier than hover/pressed, because it has to read
as a solid object rather than as a wash over the pane.

**Popup surface** — these four are deliberately **not** overlays. Everything
above is a mid-gray wash that works because there is a pane behind it. A Popup
renders in its own `HwndSource` with nothing behind it, so it must be opaque and
state its own colours:

| Key | Value | Purpose |
|-----|-------|---------|
| `PopupBrush` | `#FF2B2B2B` | ToolTip and menu surface |
| `PopupBorderBrush` | `#FF3F3F3F` | Its hairline |
| `PopupTextBrush` | `#FFF0F0F0` | Text on it |
| `PopupHoverBrush` | `#22FFFFFF` | A highlighted menu row |

Note the ComboBox drop-down is the exception that proves the rule: it mirrors
the *host's* background instead, because it is a list of the pane's own content
rather than a floating surface.

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

### 4b. The rest of the controls

| File | Keyed styles |
|------|--------------|
| `TextInputStyles.xaml` | `OfficeTextBox`, `FlatTextBox`, `OfficePasswordBox` |
| `TextStyles.xaml` | `OfficeTextBlock`, `SectionLabel`, `SecondaryText`, `OfficeLabel`, `OfficeSeparator`, `OfficeHyperlink` |
| `ListStyles.xaml` | `OfficeListBox` + item, `BorderedListBox`, `OfficeListView` + item |
| `SelectionControlStyles.xaml` | `OfficeRadioButton` |
| `IndicatorStyles.xaml` | `OfficeProgressBar`, `OfficeToolTip`, `OfficeSlider` |
| `ContainerStyles.xaml` | `OfficeExpander`, `OfficeGroupBox`, `OfficeTabControl` + item, `ChevronToggle` |
| `MenuStyles.xaml` | `OfficeMenu`, `OfficeMenuItem`, `OfficeContextMenu` |
| `TreeViewStyles.xaml` | `OfficeTreeView` + item, `TreeExpandToggle` |

Two rules these follow that the older task-pane styles do not:

**Foreground is inherited, not bound to an ancestor `UserControl`.** That
binding finds nothing in a plain `Window`, and a general library has to work in
both hosts. Emphasis uses `Opacity`, which stays correct on light and dark.

**Popups state their own colours.** `ToolTip`, `ContextMenu` and submenus live
in their own `HwndSource`, outside the pane's visual tree, so they inherit
nothing and commit to a dark surface. `OfficeMenuItem` picks its foreground by
`Role`, because a top-level `Menu` header sits on the pane background and must
inherit it — getting that wrong renders white on white.

### 5. `CheckBoxStyles.xaml` — VSCode-Style Checkbox

Implicit `CheckBox`: 14×14 square box, 2px corner radius, 1.5px vector tick.

### 6. `UpDownTextBoxStyles.xaml` — WpfLib's Own Controls

Implicit styles for `UpDownTextBox` and `UpDownFloatTextBox`, which are declared
in `WpfLib/Controls`. A control's look belongs with the control.

These are implicit styles rather than a `generic.xaml` default style: the
assembly has no `ThemeInfo` attribute, and everything else here is picked up by
merging the palette. One mechanism, not two.

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

## Shared Converters

`WpfLib/Converters/` is the same idea for `IValueConverter`. Generic ones live
there: `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`,
`IntToVisibilityConverter`, `ColorToBrushConverter`,
`BoolToFlowDirectionConverter`, `ReverseBoolConverter` and the rest.

Domain converters stay local — Nakdan's `GenreDescriptionConverter` and
`OpacityConverter`, for instance.

---

## The Gallery

`WpfLib.Gallery` renders every style on one screen, with a switch for the four
Office themes. Run it after changing a template.

It is not decoration. Its first run found six defects that all looked fine as
source: a named `RotateTransform` cannot be a trigger's `TargetName`,
`TextBlock.Opacity` is not an attached property, a `Binding` inside a
`Storyboard` throws `XamlParseException`, the checkbox tick had **never**
rendered in any host, top-level menu headers were white on white, and the
`ComboBox` placeholder had markup but no trigger.

---

## Looked At, Deliberately Not Shared

Recorded so the same candidates are not re-investigated:

- **`TextBlock`** — only DocDesignLib defines one at dictionary level. What look
  like copies elsewhere are inline `<TextBlock.Style>` blocks on single elements,
  driving status text with `DataTrigger`s. Not duplication.
- **`Separator`** — DocDesignLib uses `BorderBrush` at `Margin="0,2"`; the ribbon
  pane uses the pane foreground at 20% opacity, `Margin="0,8"`. Two surfaces, two
  intents.
- **`Path` in WebSitesLib** — sets size only (12×12). `IconPath` owns appearance;
  sizing stays with the caller.
- **`TextBox` / `ListBox`** — each styled in exactly one library.

---

## Usage

**The whole look** — a task pane. Merge the palette first, then your own styles:

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

**Only what you need** — a dialog, a standalone window, anything that already has
its own control styling:

```xml
<ResourceDictionary.MergedDictionaries>
    <!-- names only; changes nothing -->
    <ResourceDictionary Source="/WpfLib;component/themes/tokens.xaml"/>
    <!-- keyed styles; applies to nothing until you ask -->
    <ResourceDictionary Source="/WpfLib;component/themes/buttonstyles.xaml"/>
</ResourceDictionary.MergedDictionaries>
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

## The Metrics

Written down because they had drifted: buttons and toggles had different
corners, the same chevron was drawn at two sizes, and four kinds of row had
four paddings.

| | value | applies to |
|---|---|---|
| **Corner radius** | **4** | controls and surfaces: buttons, toggles, inputs, combo, group box, tooltip, menu and drop-down popups |
| | **2** | small marks and rows: the checkbox tick box, list/tree/menu rows, progress and slider tracks |
| | 7 | the radio button only, because 7 on a 14px box is a circle |
| **Border** | 1 or 0 | bordered or flat. Nothing in between |
| **Row padding** | **8,5** | every selectable row: list, combo, tree, menu |
| **Input height** | **28** | TextBox, PasswordBox, ComboBox |
| **Chevron** | **8×8**, stroke 1.5 | expander, tree, submenu. The combo's is 10×6 because it points down, not right |
| **Selection box** | 14×14 | CheckBox and RadioButton, so the two line up in a column |
| **Disabled** | Opacity 0.35 | everywhere, no exceptions |

**Interaction states go by kind, not by control.** Fill-hover on buttons, rows
and containers is `HoverBrush`; border-hover on anything bordered is
`BorderStrong`; accent-hover on links and the slider is `AccentHover`. A new
control should pick the row it belongs to rather than inventing a value.

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
