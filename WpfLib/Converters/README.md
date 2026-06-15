# Converters

WPF value converters for data binding transformations.

## Converters

- `MultiplyConverter` — Multiplies numeric values by a specified factor
- `HeightToCornerRadiusConverter` — Converts height values to corner radius
- `StringToBoolConverter` — Converts string to boolean
- `ReverseBoolConverter` — Inverts boolean values
- `ListToStringConverter` — Converts lists to delimited strings
- `FlowDirectionConverter` — Handles RTL/LTR flow direction
- `BoolToFlowDirectionConverter` — Maps boolean to FlowDirection
- `ArrayToStringConverter` — Converts arrays to strings

**Usage in XAML:**
```xml
<Window.Resources>
  <local:StringToBoolConverter x:Key="StringToBool" />
</Window.Resources>

<CheckBox IsChecked="{Binding Value, Converter={StaticResource StringToBool}}" />
```
