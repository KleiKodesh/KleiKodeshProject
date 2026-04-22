---
inclusion: auto
description: Theme-aware background and foreground management in ElementHost WPF controls. Covers when to use Transparent vs binding, popup background patterns, opacity overlays, and the two-step pattern for custom controls with popups.
---

# Theming and Background Management in ElementHost

## How Theme Colors Flow

```
Office Application (Word)
  ↓ (OfficeThemeWatcher detects theme change)
ElementHost
  ↓ (sets Background and Foreground)
UserControl (RegexFindView)
  ↓ (inherited by all child elements)
All controls inside UserControl
```

**The UserControl is the theme boundary.** ElementHost sets `UserControl.Background` and `UserControl.Foreground` based on the Office theme (light/dark/black). All controls inside inherit these values automatically.

---

## When to Use Transparent vs Binding

### Use `Background="Transparent"` (Default)

**For any control inside the UserControl that doesn't cross a window boundary:**

```xml
<!-- ✓ Input wrapper border — inside UserControl -->
<Border Background="Transparent" BorderBrush="{StaticResource BorderBrush}">
    <TextBox Background="Transparent"/>
</Border>

<!-- ✓ Button in template — inside UserControl -->
<Button Background="Transparent">
    <ContentPresenter/>
</Button>

<!-- ✓ Any panel or container -->
<StackPanel Background="Transparent">
    <TextBlock Text="Hello"/>
</StackPanel>
```

**Why it works:**
- The UserControl already has the theme background
- Transparent lets the parent background show through
- No binding evaluation overhead
- Simpler, cleaner XAML

### Bind to UserControl Background (Special Cases Only)

**1. Popup Controls (Separate HwndSource)**

Popups render in a separate window and cannot inherit from the visual tree.

```xml
<!-- ✓ History dropdown popup -->
<Popup>
    <Border Background="{Binding Background,
                RelativeSource={RelativeSource AncestorType=UserControl}}">
        <ItemsControl ItemsSource="{Binding Items}"/>
    </Border>
</Popup>

<!-- ✓ ComboBox dropdown popup -->
<Popup>
    <Border Background="{Binding Background,
                RelativeSource={RelativeSource TemplatedParent}}">
        <ItemsPresenter/>
    </Border>
</Popup>
```

**Why binding is required:**
- Popup is in a separate `HwndSource` (separate window)
- Cannot inherit Background through visual tree
- Must capture the value via binding before popup opens

**2. Custom Control with Popup (Two-Step Pattern)**

When a custom control has a popup in its template, use the two-step pattern:

```xml
<!-- Step 1: Style setter captures UserControl background as local value -->
<Style TargetType="local:ColorPickerButton">
    <Setter Property="Background" Value="{Binding Background,
        RelativeSource={RelativeSource AncestorType=UserControl}}"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="local:ColorPickerButton">
                <!-- Step 2: Popup binds to TemplatedParent (the control instance) -->
                <Popup>
                    <Border Background="{TemplateBinding Background}">
                        <!-- content -->
                    </Border>
                </Popup>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

**Why this works:**
1. Style setter binding evaluates when control is created (in main window)
2. `AncestorType=UserControl` can walk the visual tree successfully
3. Control's Background property gets a **local value** (not inherited)
4. When popup opens in separate window, `TemplatedParent` binds to the control instance
5. Control has a concrete Background value, so binding succeeds

**Why `TemplatedParent` alone doesn't work:**
- If control's Background is not explicitly set, it's `null` (not inherited)
- Inherited values don't cross window boundaries
- Style setter ensures Background is a local value on the control instance

---

## Foreground Inheritance

Foreground works differently — it inherits automatically through the visual tree, even without explicit bindings.

### Use Inherited Foreground (Default)

```xml
<!-- ✓ TextBlock inherits foreground automatically -->
<TextBlock Text="Hello"/>

<!-- ✓ Path in button inherits from button -->
<Button>
    <Path Data="{StaticResource Icon.Search}"
          Fill="{Binding Foreground,
              RelativeSource={RelativeSource AncestorType=Button}}"/>
</Button>
```

---

## Icon Usage Pattern

Icons are `StreamGeometry` resources on a 24×24 viewBox (defined in `Icons.xaml`). Render them with a plain `Path` — no `Viewbox` or `Canvas` wrapper needed.

```xml
<!-- ✓ CORRECT — Path with Stretch="Uniform" scales itself -->
<Path Data="{StaticResource Icon.Search}"
      Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=UserControl}}"
      Stretch="Uniform"
      FlowDirection="LeftToRight"
      Width="16" Height="16"/>

<!-- ✗ WRONG — unnecessary wrapper elements -->
<Viewbox Width="16" Height="16" FlowDirection="LeftToRight">
    <Canvas Width="24" Height="24">
        <Path Fill="..." Data="{StaticResource Icon.Search}"/>
    </Canvas>
</Viewbox>
```

`FlowDirection="LeftToRight"` is required on the `Path` itself when the parent is RTL — otherwise the geometry mirrors horizontally.

For icons inside button content areas where the button style already sets `Path.Fill` via `Style.Resources`, you can omit the `Fill` binding:

```xml
<!-- DocDesign buttons — implicit Path style in ButtonStyles.xaml sets Fill + Stretch -->
<Button>
    <Path Data="{StaticResource Icon.AlignColumns}"/>
</Button>
```

### Bind Foreground Explicitly (When Needed)

```xml
<!-- ✓ Control template needs explicit foreground -->
<ControlTemplate TargetType="Button">
    <Border>
        <ContentPresenter Foreground="{TemplateBinding Foreground}"/>
    </Border>
</ControlTemplate>

<!-- ✓ Style setter for custom control -->
<Style TargetType="local:MyControl">
    <Setter Property="Foreground" Value="{Binding Foreground,
        RelativeSource={RelativeSource AncestorType=UserControl}}"/>
</Style>
```

---

## Opacity Overlays for Secondary Colors

Never hardcode light-mode colors. Use mid-gray (`#808080`) opacity overlays that work on both light and dark themes.

```xml
<!-- ✓ Secondary background — 6% gray overlay -->
<SolidColorBrush x:Key="BgSecBrush" Color="#0F808080"/>

<!-- ✓ Hover state — 4% gray overlay -->
<SolidColorBrush x:Key="HoverBrush" Color="#0A808080"/>

<!-- ✓ Border — 31% gray overlay -->
<SolidColorBrush x:Key="BorderBrush" Color="#50808080"/>

<!-- ✗ NEVER hardcode light-mode colors -->
<SolidColorBrush x:Key="BadBorder" Color="#E1DFDD"/>  <!-- breaks in dark mode -->
```

**Why mid-gray works:**
- `#808080` is equidistant from black (`#000000`) and white (`#FFFFFF`)
- On white background: gray overlay creates subtle dark tint
- On dark background: gray overlay creates subtle light tint
- Same opacity value works on both themes

---

## Common Patterns

### Input Controls

```xml
<!-- Border with transparent background -->
<Border Background="Transparent" BorderBrush="{StaticResource BorderBrush}">
    <TextBox Background="Transparent"
             Foreground="{Binding Foreground,
                 RelativeSource={RelativeSource AncestorType=UserControl}}"/>
</Border>
```

### Buttons

```xml
<Button Background="Transparent">
    <Button.Template>
        <ControlTemplate TargetType="Button">
            <Border x:Name="Bd" Background="Transparent">
                <ContentPresenter/>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Bd" Property="Background"
                            Value="{StaticResource HoverBrush}"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
    </Button.Template>
</Button>
```

### ComboBox (Implicit Style)

```xml
<Style TargetType="ComboBox">
    <!-- Capture UserControl background as local value -->
    <Setter Property="Background" Value="{Binding Background,
        RelativeSource={RelativeSource AncestorType=UserControl}}"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ComboBox">
                <Border Background="{TemplateBinding Background}">
                    <!-- ... -->
                </Border>
                <!-- Popup binds to TemplatedParent -->
                <Popup>
                    <Border Background="{Binding Background,
                            RelativeSource={RelativeSource TemplatedParent}}">
                        <ItemsPresenter/>
                    </Border>
                </Popup>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

---

## Decision Tree

```
Does the control cross a window boundary (Popup, ContextMenu)?
├─ YES → Bind Background to UserControl or TemplatedParent
│         (see two-step pattern for custom controls with popups)
└─ NO → Use Background="Transparent"

Does the control need a specific foreground color?
├─ YES → Bind Foreground explicitly
└─ NO → Let it inherit automatically

Is this a secondary surface (panel, section background)?
├─ YES → Use opacity overlay brush (BgSecBrush, BgTerBrush)
└─ NO → Use Transparent

Is this a border or separator?
├─ YES → Use BorderBrush or BorderStrong (opacity overlays)
└─ NO → Use Transparent
```

---

## Anti-Patterns

### ✗ Binding Background When Transparent Works

```xml
<!-- ✗ BAD — unnecessary binding -->
<Border Background="{Binding Background,
    RelativeSource={RelativeSource AncestorType=UserControl}}">
    <TextBox/>
</Border>

<!-- ✓ GOOD — simpler, same effect -->
<Border Background="Transparent">
    <TextBox/>
</Border>
```

### ✗ Hardcoding Light-Mode Colors

```xml
<!-- ✗ BAD — breaks in dark mode -->
<Border Background="#F3F2F1" BorderBrush="#E1DFDD">

<!-- ✓ GOOD — adaptive opacity overlays -->
<Border Background="{StaticResource BgSecBrush}"
        BorderBrush="{StaticResource BorderBrush}">
```

### ✗ Popup Without Background Binding

```xml
<!-- ✗ BAD — popup will have black background in dark mode -->
<Popup>
    <Border Background="Transparent">
        <ItemsControl ItemsSource="{Binding Items}"/>
    </Border>
</Popup>

<!-- ✓ GOOD — popup gets theme background -->
<Popup>
    <Border Background="{Binding Background,
            RelativeSource={RelativeSource AncestorType=UserControl}}">
        <ItemsControl ItemsSource="{Binding Items}"/>
    </Border>
</Popup>
```

### ✗ Binding Foreground Everywhere

```xml
<!-- ✗ BAD — unnecessary binding, foreground inherits automatically -->
<TextBlock Text="Hello"
           Foreground="{Binding Foreground,
               RelativeSource={RelativeSource AncestorType=UserControl}}"/>

<!-- ✓ GOOD — let it inherit -->
<TextBlock Text="Hello"/>
```

---

## Performance Notes

- **Transparent is faster** than binding — no binding evaluation overhead
- **Inherited foreground is faster** than explicit binding
- **StaticResource is faster** than DynamicResource (15-25% overhead)
- Use bindings only when necessary (popups, custom controls with popups)

---

## Related Files

- `Themes/Brushes.xaml` — Opacity overlay brush definitions
- `Themes/ComboBoxStyles.xaml` — ComboBox two-step pattern example
- `Themes/ColorPickerStyles.xaml` — Custom control with popup example
- `.kiro/steering/wpf/05-elementhost-vsto.md` — Full ElementHost/VSTO guide
