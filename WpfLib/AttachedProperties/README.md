# AttachedProperties — WPF Attached Behavior Properties

Attached properties that extend standard WPF element capabilities without subclassing.

## Files

### `ButtonStripBehavior.cs`
Groups adjacent buttons into a visual strip. Removes inner borders and rounds outer corners, creating a seamless button group like a toolbar segment.

Attached properties:
- `ButtonStrip.GroupName` — Buttons with the same GroupName are treated as a strip
- Used in DocDesignLib and RegexFindLib toolbars

### `GridSetup.cs`
Allows configuring Grid row/column definitions from code via attached properties. Useful when generating Grid layouts dynamically.

Attached properties:
- `GridSetup.RowDefinitions` — Semi-colon-delimited row height spec
- `GridSetup.ColumnDefinitions` — Semi-colon-delimited column width spec
- String format: `"Auto;*;2*;50"` (same as XAML GridLength)

### `PopupListBoxBehaviour.cs`
Attached behavior that makes a ListBox behave like a popup/overlay. Handles:
- Click-outside-to-close
- Keyboard navigation (Escape closes)
- Positioning relative to placement target
Used in WebSitesLib for the address bar dropdown.

### `TextBoxBehaviours.cs`
Attached behaviors for TextBox:

- `SelectAllOnFocus` (bool) — Selects all text when TextBox receives focus
- `Watermark` (string) — Shows watermark text when TextBox is empty
- `NumericOnly` (bool) — Blocks non-numeric input
- `MaxLength` (int) — Overrides TextBox.MaxLength with different validation semantics

## Usage

```xml
<TextBox local:TextBoxBehaviours.SelectAllOnFocus="True"
         local:TextBoxBehaviours.Watermark="הקלד טקסט..."
         local:TextBoxBehaviours.NumericOnly="True" />
```
