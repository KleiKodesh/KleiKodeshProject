# Converters — WPF Value Converters

Value converters for WPF data binding, used across all task pane libraries.

## Files

**`FlowDirectionConverter.cs`** — Converts string `"RTL"`/`"LTR"` to `FlowDirection` enum. Used for dynamic RTL switching in Hebrew UI.

**`BoolToFlowDirectionConverter.cs`** — Converts `bool` → `FlowDirection` (true = RTL, false = LTR). Used for binding RTL settings to layout.

**`ReverseBoolConverter.cs`** — Inverts a boolean value. Used for visibility toggles and inverse bindings.

**`StringToBoolConverter.cs`** — Non-empty string → true, null/empty → false. Used for showing/hiding elements based on text content.

**`ListToStringConverter.cs`** — `IEnumerable` → delimited string. Optional `Separator` parameter. Used for displaying list selections.

**`ArrayToStringConverter.cs`** — `object[]` → delimited string. Similar to ListToString but for arrays.

**`ToggleCheckBoxConverters.cs`** — Multi-value converter for three-state `CheckBox` toggles. Handles the indeterminate state logic for format toggles in RegexFindLib.

## Usage

```xml
<Window.Resources>
    <local:ReverseBoolConverter x:Key="ReverseBool" />
    <local:FlowDirectionConverter x:Key="FlowDir" />
</Window.Resources>

<CheckBox IsChecked="{Binding IsHidden, Converter={StaticResource ReverseBool}}" />
<FlowDocument FlowDirection="{Binding Lang, Converter={StaticResource FlowDir}}" />
```
