---
inclusion: fileMatch
fileMatchPattern: "**/*.xaml,**/*ViewModel*.cs,**/*viewmodel*.cs,**/*Viewmodel*.cs"
---

# WPF Custom Controls

## Control vs UserControl

**Use `Control` with `ControlTemplate` for reusable controls.**  
**Use `UserControl` only for one-off composite views that will never be restyled.**

`UserControl` bakes visual structure into the class — consumers can't restyle it.  
`Control` keeps appearance fully replaceable via `Style`/`Template`.

---

## Custom Control Pattern (ElementHost/VSTO)

**Note:** `Themes/Generic.xaml` is NOT loaded in ElementHost. Define templates in a `ResourceDictionary` merged by the host view.

### 1. C# Class — Logic, DPs, Template Parts

```csharp
[TemplatePart(Name = "PART_Button", Type = typeof(Button))]
public class ColorPickerButton : Control
{
    // Dependency Property
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color?),
            typeof(ColorPickerButton),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((ColorPickerButton)d).OnColorChanged()));

    public Color? SelectedColor
    {
        get => (Color?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    void OnColorChanged() { /* update derived properties */ }

    // Template part wiring
    Button _button;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        // Unwire old
        if (_button != null) _button.Click -= OnButtonClick;
        
        // Wire new
        _button = GetTemplateChild("PART_Button") as Button;
        if (_button != null) _button.Click += OnButtonClick;
    }

    void OnButtonClick(object s, RoutedEventArgs e) { /* ... */ }
}
```

**Key Rules:**
- No `.xaml` file — plain `.cs` only
- All state in `DependencyProperty` declarations
- Use `BindsTwoWayByDefault` for input properties
- Always unwire old handlers before wiring new ones

### 2. XAML Template — In ResourceDictionary

```xml
<ResourceDictionary xmlns:local="clr-namespace:MyApp.UI">
    <Style TargetType="local:ColorPickerButton">
        <Setter Property="Width" Value="26"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="local:ColorPickerButton">
                    <Button x:Name="PART_Button" Background="Transparent">
                        <ContentPresenter/>
                    </Button>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

### 3. Data-Driven Content — ItemsControl + DataTemplate

**Never build UI lists in C# code.** Expose collections and bind in XAML.

```csharp
public IReadOnlyList<WordColor> ThemeColors => WordColors.ThemeColors;
```

```xml
<ItemsControl ItemsSource="{Binding ThemeColors,
    RelativeSource={RelativeSource AncestorType=local:ColorPickerButton}}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Width="18" Height="18" Background="{Binding WpfColor}"/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### 4. User Interactions — Commands, Not Attached Behaviors

**CRITICAL: Prefer ICommand properties over attached behaviors for user interactions.**

Custom controls should expose `ICommand` properties that can be bound from the template. This is cleaner, more testable, and follows standard WPF patterns.

**WRONG — Attached Behavior Pattern:**
```csharp
// Separate behavior class with event handlers
public static class SwatchBehavior
{
    public static readonly DependencyProperty ColorProperty = ...;
    
    static void OnClick(object sender, MouseButtonEventArgs e)
    {
        var picker = FindAncestor<ColorPickerButton>((DependencyObject)sender);
        picker?.SelectColor(GetColor((DependencyObject)sender));  // Walks tree, calls internal method
    }
}
```

```xml
<!-- XAML uses attached property -->
<Border local:SwatchBehavior.Color="{Binding WpfColor}"/>
```

**Problems:**
- Mixes event handling with visual tree walking
- Requires `internal` methods on control
- Not testable without UI
- Not reusable outside this specific control
- Violates separation of concerns

**CORRECT — Command Pattern:**
```csharp
public class ColorPickerButton : Control
{
    public ColorPickerButton()
    {
        SelectColorCommand = new RelayCommand<Color?>(ExecuteSelectColor);
    }

    public ICommand SelectColorCommand { get; }

    void ExecuteSelectColor(Color? color)
    {
        SelectedColor = color;
        if (_popup != null) _popup.IsOpen = false;
    }

    // Simple inline RelayCommand
    class RelayCommand<T> : ICommand
    {
        readonly Action<T> _execute;
        public RelayCommand(Action<T> execute) => _execute = execute;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute((T)parameter);
    }
}
```

```xml
<!-- XAML binds command with parameter -->
<Border>
    <Border.InputBindings>
        <MouseBinding MouseAction="LeftClick"
                      Command="{Binding SelectColorCommand,
                          RelativeSource={RelativeSource AncestorType=local:ColorPickerButton}}"
                      CommandParameter="{Binding WpfColor}"/>
    </Border.InputBindings>
</Border>
```

**Benefits:**
- Standard WPF pattern (same as Button.Command)
- Bindable from XAML with parameters
- Testable — invoke command directly
- No visual tree walking
- Clean separation — all logic in control
- Can implement `CanExecute` for enable/disable logic

**When to use attached behaviors:**
- Cross-cutting concerns (focus management, drag-drop)
- Enhancing existing controls you don't own
- Truly reusable behaviors across many control types

**When to use commands:**
- User interactions in your own custom controls (clicks, selections)
- Any action that should be bindable and testable
- **Default choice for custom control interactions**

### 5. Use Click Events, Not MouseDown

**Always use `Click` / `MouseLeftButtonUp`, never `MouseDown`.**

- Fires only when released over same element (proper click semantics)
- Supports mouse and touch automatically
- `MouseDown` fires on press before gesture completes

### 6. Internal Methods — Avoid Them

**Avoid `internal` methods that exist only to be called by attached behaviors.**

If you find yourself writing `internal void DoSomething()` to be called from an attached behavior, you should be using a command instead.

```csharp
// BAD — internal method for behavior
internal void SelectColor(Color? color)
{
    SelectedColor = color;
}

// GOOD — command property
public ICommand SelectColorCommand { get; }
```

### 7. No ViewModels in Custom Controls

Controls are the View layer:
- State in `DependencyProperty` declarations
- Logic in control class
- No `INotifyPropertyChanged`
- No `ICommand` properties

The consumer's ViewModel binds to the control's DPs.

---

## Dependency Properties — Complete Pattern

```csharp
// 1. Static readonly field
public static readonly DependencyProperty ValueProperty =
    DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(MySlider),
        new FrameworkPropertyMetadata(
            0.0,                                                // Default
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,                                     // Change callback
            CoerceValue),                                       // Coerce callback
        IsValidValue);                                          // Validate callback

// 2. CLR wrapper — ONLY GetValue/SetValue
public double Value
{
    get => (double)GetValue(ValueProperty);
    set => SetValue(ValueProperty, value);
}

// 3. Change callback — static
static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    ((MySlider)d).OnValueChanged((double)e.OldValue, (double)e.NewValue);
}

// 4. Virtual instance method
protected virtual void OnValueChanged(double oldValue, double newValue)
{
    RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, newValue));
}

// 5. Coerce — constrain value
static object CoerceValue(DependencyObject d, object value)
{
    var ctrl = (MySlider)d;
    return Math.Max(ctrl.Minimum, Math.Min(ctrl.Maximum, (double)value));
}

// 6. Validate — reject invalid
static bool IsValidValue(object value)
{
    double d = (double)value;
    return !double.IsNaN(d) && !double.IsInfinity(d);
}
```

**Critical:** CLR wrapper must contain ONLY `GetValue`/`SetValue`. Bindings bypass it.

### Read-Only Dependency Properties

```csharp
static readonly DependencyPropertyKey IsPressedPropertyKey =
    DependencyProperty.RegisterReadOnly(nameof(IsPressed), typeof(bool), ...);

public static readonly DependencyProperty IsPressedProperty =
    IsPressedPropertyKey.DependencyProperty;

public bool IsPressed
{
    get => (bool)GetValue(IsPressedProperty);
    protected set => SetValue(IsPressedPropertyKey, value);  // Uses KEY
}
```

---

## Routed Events

```csharp
// 1. Register
public static readonly RoutedEvent ValueChangedEvent =
    EventManager.RegisterRoutedEvent(
        "ValueChanged",
        RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<double>),
        typeof(MySlider));

// 2. CLR wrapper
public event RoutedPropertyChangedEventHandler<double> ValueChanged
{
    add    => AddHandler(ValueChangedEvent, value);
    remove => RemoveHandler(ValueChangedEvent, value);
}

// 3. Raise
protected virtual void OnValueChanged(double oldValue, double newValue)
{
    RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
        oldValue, newValue, ValueChangedEvent));
}
```

---

## OnApplyTemplate — Template Part Contract

```csharp
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
public class MyControl : Control
{
    const string PartTextBox = "PART_TextBox";
    TextBox _textBox;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // 1. Detach old
        if (_textBox != null) _textBox.TextChanged -= OnTextChanged;

        // 2. Get new
        _textBox = GetTemplateChild(PartTextBox) as TextBox;

        // 3. Attach new
        if (_textBox != null) _textBox.TextChanged += OnTextChanged;

        // 4. Sync state
        UpdateVisuals();
    }
}
```

**Rules:**
- Always null-check after `GetTemplateChild`
- Always detach before re-attaching
- Use `const string` for part names
- Declare `[TemplatePart]` attributes

---

## Template Best Practices

**Disabled State — Use Opacity:**
```xml
<Trigger Property="IsEnabled" Value="false">
    <Setter TargetName="border" Property="Opacity" Value="0.56"/>
</Trigger>
```

**SnapsToDevicePixels — Always on Root:**
```xml
<Border x:Name="border" SnapsToDevicePixels="true">...</Border>
```

**ContentPresenter Rules:**
```xml
<ContentPresenter
    RecognizesAccessKey="True"
    Focusable="False"
    Margin="{TemplateBinding Padding}"
    HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
    SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"/>
```

**Trigger Targets — Named Elements:**
```xml
<!-- WRONG -->
<Trigger Property="IsMouseOver" Value="True">
    <Setter Property="Background" Value="#0A808080"/>
</Trigger>

<!-- CORRECT -->
<Trigger Property="IsMouseOver" Value="True">
    <Setter TargetName="border" Property="Background" Value="#0A808080"/>
</Trigger>
```
