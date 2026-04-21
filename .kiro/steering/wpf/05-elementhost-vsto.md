# WPF in ElementHost / VSTO

## Theme-Aware Colors (Dark Mode)

Host sets `UserControl.Background` and `UserControl.Foreground` from Office theme. All controls inherit automatically.

**Never hardcode light-mode colors** (`#1A1A1A`, `#F3F2F1`, `#FFFFFF`).

**Use mid-gray opacity overlays** — `#808080` is equidistant from black and white:

| Use | Value |
|-----|-------|
| Border | `#50808080` |
| Hover background | `#0A808080` |
| Pressed background | `#14808080` |
| Secondary surface | `#0F808080` |
| Strong border | `#80808080` |

```xml
<Border BorderBrush="#50808080" BorderThickness="1"/>

<Trigger Property="IsMouseOver" Value="True">
    <Setter Property="Background" Value="#0A808080"/>
</Trigger>
```

---

## When to Use Transparent vs Binding

### Use `Background="Transparent"` (Default)

For any control inside the UserControl that doesn't cross a window boundary:

```xml
<!-- ✓ Input wrapper border — inside UserControl -->
<Border Background="Transparent" BorderBrush="{StaticResource BorderBrush}">
    <TextBox Background="Transparent"/>
</Border>

<!-- ✓ Button in template — inside UserControl -->
<Button Background="Transparent">
    <ContentPresenter/>
</Button>
```

**Why it works:** The UserControl already has the theme background. Transparent lets the parent background show through naturally.

### Bind to UserControl Background (Special Cases Only)

**Only for controls that cross window boundaries** (Popup, ContextMenu):

```xml
<!-- ✓ History dropdown popup -->
<Popup>
    <Border Background="{Binding Background,
                RelativeSource={RelativeSource AncestorType=UserControl}}">
        <ItemsControl ItemsSource="{Binding Items}"/>
    </Border>
</Popup>
```

**Why binding is required:** Popup is in a separate `HwndSource` (separate window) and cannot inherit from the visual tree.

**Foreground inheritance:**
**Foreground inheritance:**

Foreground inherits automatically through the visual tree. Explicit binding is only needed in control templates:

```xml
<!-- ✓ TextBlock inherits automatically -->
<TextBlock Text="Hello"/>

<!-- ✓ Explicit binding in control template -->
<ControlTemplate TargetType="Button">
    <ContentPresenter Foreground="{TemplateBinding Foreground}"/>
</ControlTemplate>
```

---

## Popup Background

`Popup` is in separate HwndSource — cannot inherit `Background` from visual tree.

**The Solution: Set Background in the Control's Style**

Custom controls must have their `Background` property explicitly set (via style setter) to a binding that resolves **before** the popup opens. The popup then binds to `TemplatedParent.Background`.

```xml
<!-- In ColorPickerStyles.xaml -->
<Style TargetType="local:ColorPickerButton">
    <Setter Property="Background" Value="{Binding Background,
        RelativeSource={RelativeSource AncestorType=UserControl}}"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="local:ColorPickerButton">
                <Popup AllowsTransparency="False">
                    <Border Background="{Binding Background,
                        RelativeSource={RelativeSource TemplatedParent}}">
                        <!-- content -->
                    </Border>
                </Popup>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

**Why This Works:**

1. **Style setter binding** is evaluated when the control is created (in the main window)
2. At that point, `AncestorType=UserControl` can walk the visual tree successfully
3. The `ColorPickerButton.Background` property gets the UserControl's background value
4. When the popup opens in a separate window, `TemplatedParent` binds to the `ColorPickerButton` instance
5. The `ColorPickerButton` has a concrete Background value (not inherited), so the binding works

**Why `TemplatedParent` Alone Doesn't Work:**

If the control's Background is not explicitly set, it's `null` (not inherited). Inherited values don't cross window boundaries. The style setter ensures the Background is a **local value** on the control instance.

**ComboBox Pattern (for reference):**

```xml
<Style TargetType="ComboBox">
    <Setter Property="Background" Value="{Binding Background,
        RelativeSource={RelativeSource AncestorType=UserControl}}"/>
    <!-- ... -->
</Style>

<ControlTemplate TargetType="ComboBox">
    <Popup>
        <Border Background="{Binding Background,
            RelativeSource={RelativeSource TemplatedParent}}">
    </Popup>
</ControlTemplate>
```

**Key Principle:**

Popup backgrounds must bind to a **local value** (not inherited) on a control that exists in the template. Use a style setter with `AncestorType` binding to capture the theme background as a local value before the popup opens.

**Rules:**
- Use `AllowsTransparency="False"` — `True` breaks background in dark mode
- Set control's Background in style setter with `AncestorType=UserControl` binding
- Popup border binds to `TemplatedParent.Background`
- Never use `Transparent` for popup backgrounds — they need the actual theme color

---

## Dispatcher in VSTO

`Application.Current` is `null` in VSTO. Always capture dispatcher before async:

```csharp
readonly Dispatcher _dispatcher =
    Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

Task.Run(() =>
{
    var data = LoadData();
    _dispatcher.BeginInvoke(new Action(() => UpdateCollection(data)));
});
```

**Capture in constructor, never in background thread.**

---

## Generic.xaml Not Loaded

`DefaultStyleKeyProperty.OverrideMetadata` + `Generic.xaml` doesn't work in ElementHost.

**Instead:** Define templates in `ResourceDictionary` merged into root `UserControl`:

```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="pack://application:,,,/MyLib;component/Themes/Controls.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</UserControl.Resources>
```

---

## Theme Inheritance

**What inherits automatically:**
- `Foreground` — text color
- `Background` — surface color
- `FontFamily`, `FontSize`

**What does NOT inherit:**
- `Popup` backgrounds (separate HwndSource)
- `ContextMenu` backgrounds (separate HwndSource)

**Fix:** Bind to ancestor `UserControl` or `TemplatedParent`.

---

## DataContext Binding Pattern

```xml
<UserControl 
    x:Name="This"
    DataContext="{Binding ElementName=This}"
    >
    <TextBlock Text="{Binding MyProperty}"/>
</UserControl>
```

```csharp
public partial class MyControl : UserControl
{
    public static readonly DependencyProperty MyPropertyProperty = ...;
    
    public string MyProperty
    {
        get => (string)GetValue(MyPropertyProperty);
        set => SetValue(MyPropertyProperty, value);
    }
}
```

Exposes properties as DPs, bindable from XAML.
