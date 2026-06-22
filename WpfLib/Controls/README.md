# Controls — Custom WPF Controls

Custom WPF controls for numeric input with spinner buttons.

## Files

### `UpDownTextBox.cs`
Integer-only TextBox with up/down arrow buttons. Features:
- Configurable `MinValue` / `MaxValue` (DependencyProperties)
- `Value` property (int) with change notification
- Keyboard: Up/Down arrows increment/decrement
- Wrap-around optional (can be disabled for bounded ranges)
- Used by DocDesignLib for spacing/indent numeric inputs

### `UpDownFloatTextBox.cs`
Float variant of UpDownTextBox. Features:
- `DecimalPlaces` property (DependencyProperty, default 2)
- `Value` property (double)
- Same spinner and keyboard behavior as UpDownTextBox
- Used by DocDesignLib Spacing controls for fine-tuned paragraph spacing

## Usage

```xml
<controls:UpDownTextBox Value="{Binding FontSize, Mode=TwoWay}" 
                        MinValue="8" MaxValue="72" />
<controls:UpDownFloatTextBox Value="{Binding LineSpacing, Mode=TwoWay}"
                             DecimalPlaces="1" MinValue="0.5" MaxValue="3.0" />
```
