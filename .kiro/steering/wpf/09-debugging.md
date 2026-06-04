---
inclusion: fileMatch
fileMatchPattern: "**/*.xaml,**/*ViewModel*.cs,**/*viewmodel*.cs,**/*Viewmodel*.cs"
---

# WPF Debugging

## Finding Binding Errors

```csharp
// In App.xaml.cs or debug startup
PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
PresentationTraceSources.DataBindingSource.Listeners.Add(new ConsoleTraceListener());
```

**Or in App.config:**
```xml
<system.diagnostics>
  <sources>
    <source name="System.Windows.Data" switchName="BindingSwitch">
      <listeners>
        <add name="textListener" type="System.Diagnostics.TextWriterTraceListener"
             initializeData="binding.log"/>
      </listeners>
    </source>
  </sources>
  <switches>
    <add name="BindingSwitch" value="Warning"/>
  </switches>
</system.diagnostics>
```

---

## Trace Specific Binding

```xml
<!-- Add namespace -->
xmlns:diag="clr-namespace:System.Diagnostics;assembly=WindowsBase"

<!-- Verbose trace on specific binding -->
<TextBlock Text="{Binding Name, diag:PresentationTraceSources.TraceLevel=High}"/>
```

---

## Inspecting Visual Tree at Runtime

```csharp
// Find named element inside template
var textBox = FindVisualChild<TextBox>(myControl);

// Walk up to find parent
var listBox = FindVisualParent<ListBox>(someItem);

static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
{
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T t) return t;
        var result = FindVisualChild<T>(child);
        if (result != null) return result;
    }
    return null;
}

static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
{
    var parent = VisualTreeHelper.GetParent(child);
    while (parent != null)
    {
        if (parent is T t) return t;
        parent = VisualTreeHelper.GetParent(parent);
    }
    return null;
}
```

---

## Checking Property Value Source

```csharp
// Find out WHERE a property value came from
var source = DependencyPropertyHelper.GetValueSource(
    element, FrameworkElement.BackgroundProperty);

Console.WriteLine(source.BaseValueSource);
// Outputs: Local, Style, ImplicitStyleReference, Inherited, Default, etc.

Console.WriteLine(source.IsAnimated);
Console.WriteLine(source.IsCoerced);
```

Invaluable for debugging "why is this property not what I set it to?"

---

## Common Binding Errors

**`RelativeSource FindAncestor` in DataTemplate before initialized:**
```xml
<!-- Add FallbackValue -->
<TextBlock Text="{Binding MaybeNull, FallbackValue='N/A'}"/>
```

**Binding to property that doesn't exist:**
- Check spelling
- Check `DataContext` is correct type
- Use `FallbackValue` for optional properties

**Binding to null `DataContext`:**
```xml
<TextBlock Text="{Binding Name, TargetNullValue='No data'}"/>
```

---

## Snoop — Essential Tool

[Snoop](https://github.com/snoopwpf/snoopwpf) is the standard WPF visual tree inspector.

**Install:** `winget install Snoop.Snoop`

**Features:**
- Browse live visual tree
- See all property values and sources
- Find binding errors (highlighted in red)
- Edit property values at runtime
- Trace events

---

## Visual Tree vs Logical Tree

```csharp
// Logical tree — what you declared in XAML
foreach (object child in LogicalTreeHelper.GetChildren(element))
{
    // Excludes template internals
}

// Visual tree — what's actually rendered
int count = VisualTreeHelper.GetChildrenCount(element);
for (int i = 0; i < count; i++)
{
    var child = VisualTreeHelper.GetChild(element, i);
    // Includes all template internals
}
```

**Use logical tree for:** Data/resource concerns  
**Use visual tree for:** Rendering/layout concerns, finding template parts

---

## Dependency Property Value Precedence

When a DP is set from multiple sources, WPF uses this order (highest wins):

1. Property system coercion (`CoerceValueCallback`)
2. Active animation
3. Local value (`SetValue`, XAML attribute, binding)
4. TemplatedParent triggers
5. TemplatedParent property sets
6. Implicit style
7. Style triggers
8. Template triggers
9. Style setter values
10. Default (theme) style
11. Inheritance
12. Default value from metadata

**If you set a local value, triggers can't override it.**

```csharp
// Creates LOCAL value (level 3) — overrides styles permanently
button.SetValue(Button.BackgroundProperty, Brushes.Red);

// Sets at CURRENT effective level — styles can still override
button.SetCurrentValue(Button.BackgroundProperty, Brushes.Red);

// Removes local value — next highest source takes over
button.ClearValue(Button.BackgroundProperty);
```
