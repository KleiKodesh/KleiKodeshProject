# Attached Properties & Behaviors

## Attached Property Pattern

Attached properties are dependency properties defined on one class but set on any `DependencyObject`.

```csharp
public static class ScrollBehavior
{
    // 1. Register with RegisterAttached
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(ScrollBehavior),
            new FrameworkPropertyMetadata(false, OnAutoScrollChanged));

    // 2. Static Get/Set accessors
    public static bool GetAutoScroll(DependencyObject d)
        => (bool)d.GetValue(AutoScrollProperty);

    public static void SetAutoScroll(DependencyObject d, bool value)
        => d.SetValue(AutoScrollProperty, value);

    // 3. Change callback — wires behavior
    static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            if ((bool)e.NewValue)
                sv.ScrollChanged += OnScrollChanged;
            else
                sv.ScrollChanged -= OnScrollChanged;
        }
    }

    static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0)
            ((ScrollViewer)sender).ScrollToEnd();
    }
}
```

**Usage:**
```xml
<ScrollViewer local:ScrollBehavior.AutoScroll="True">
    <StackPanel x:Name="log"/>
</ScrollViewer>
```

---

## Inheritable Attached Property

Propagates to all descendants automatically:

```csharp
public static readonly DependencyProperty ThemeProperty =
    DependencyProperty.RegisterAttached(
        "Theme", typeof(AppTheme), typeof(ThemeHelper),
        new FrameworkPropertyMetadata(
            AppTheme.Light,
            FrameworkPropertyMetadataOptions.Inherits,  // ← Key flag
            OnThemeChanged));
```

```xml
<!-- Set once at root — all children inherit -->
<UserControl local:ThemeHelper.Theme="Dark">
    <Button/>     <!-- Theme=Dark inherited -->
    <TextBox/>    <!-- Theme=Dark inherited -->
</UserControl>
```

**This is the correct alternative to `RelativeSource FindAncestor`** for pushing context values down the tree.

---

## Common Attached Behaviors

**Select All on Focus:**
```csharp
public static class TextBoxBehavior
{
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached("SelectAllOnFocus", typeof(bool),
            typeof(TextBoxBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(TextBox d) =>
        (bool)d.GetValue(SelectAllOnFocusProperty);
    public static void SetSelectAllOnFocus(TextBox d, bool v) =>
        d.SetValue(SelectAllOnFocusProperty, v);

    static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox tb)
        {
            tb.GotFocus -= OnGotFocus;
            if ((bool)e.NewValue) tb.GotFocus += OnGotFocus;
        }
    }

    static void OnGotFocus(object s, RoutedEventArgs e)
        => ((TextBox)s).SelectAll();
}
```

```xml
<TextBox local:TextBoxBehavior.SelectAllOnFocus="True"/>
```

---

## Attached Behavior vs Attached Property

| | Attached Property | Blend Behavior |
|--|-------------------|----------------|
| Definition | `RegisterAttached` | Inherits `Behavior<T>` |
| XAML | `local:Foo.Bar="value"` | `<i:Interaction.Behaviors>` |
| Bindable | Yes | Yes |
| Multiple per element | One per property | Multiple behaviors |
| Complexity | Simple | More structured |

**For this project:** Use attached properties — simpler, no Blend SDK dependency.

---

## Click Callbacks from DataTemplate

When `DataTemplate` item needs to call parent control:

```csharp
public static class SwatchBehavior
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.RegisterAttached("Color", typeof(Color?),
            typeof(SwatchBehavior),
            new PropertyMetadata(null, OnColorChanged));

    static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is UIElement el)) return;
        el.MouseLeftButtonUp -= OnClick;
        if (e.NewValue != null) el.MouseLeftButtonUp += OnClick;
    }

    static void OnClick(object sender, MouseButtonEventArgs e)
    {
        var picker = FindAncestor<ColorPickerButton>((DependencyObject)sender);
        picker?.SelectColor(GetColor((DependencyObject)sender));
        e.Handled = true;
    }

    static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        var p = VisualTreeHelper.GetParent(d);
        while (p != null)
        {
            if (p is T t) return t;
            p = VisualTreeHelper.GetParent(p);
        }
        return null;
    }
}
```

```xml
<DataTemplate>
    <Border local:SwatchBehavior.Color="{Binding WpfColor}"/>
</DataTemplate>
```

---

## Built-in Attached Properties

**Grid:**
```xml
<Button Grid.Row="0" Grid.Column="1" Grid.RowSpan="2"/>
```

**DockPanel:**
```xml
<Button DockPanel.Dock="Top"/>
```

**Canvas:**
```xml
<Button Canvas.Left="10" Canvas.Top="20"/>
```

**ScrollViewer:**
```xml
<ListBox ScrollViewer.HorizontalScrollBarVisibility="Disabled"/>
```

**VirtualizingStackPanel:**
```xml
<ListBox VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Recycling"/>
```
