# WPF Performance Best Practices

## Critical Rules

### 1. Virtualize ItemsControls — Always

```xml
<ListBox VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Recycling"/>
```

Without virtualization: 10,000 items = 10,000 `ListBoxItem` objects in memory.  
With recycling: only visible items exist.

### 2. Bind to IList, Not IEnumerable

```csharp
// SLOW — WPF creates IList wrapper
public IEnumerable<Item> Items => _items;

// FAST — direct IList binding
public ObservableCollection<Item> Items { get; } = new();
```

### 3. StaticResource vs DynamicResource

```xml
<!-- FAST — resolved once at load -->
<Button Style="{StaticResource ButtonStyle}"/>

<!-- SLOW — re-evaluated on every access (15-25% overhead) -->
<Button Style="{DynamicResource ButtonStyle}"/>
```

Use `DynamicResource` only for theme-dependent resources that change at runtime.

### 4. Freeze Freezables

```csharp
var brush = new SolidColorBrush(Colors.Red);
brush.Freeze();  // Immutable, faster, thread-safe
```

Resources in XAML are frozen automatically.

### 5. Opacity on Brush, Not Element

```xml
<!-- SLOW — creates temporary surface -->
<Rectangle Fill="Red" Opacity="0.5"/>

<!-- FAST — no temporary surface -->
<Rectangle>
    <Rectangle.Fill>
        <SolidColorBrush Color="Red" Opacity="0.5"/>
    </Rectangle.Fill>
</Rectangle>
```

### 6. Fix Binding Errors

Every binding error writes to trace log and retries resolution. 100 binding errors = measurable perf hit.

**Find them:**
```csharp
PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
```

**Fix with FallbackValue:**
```xml
<TextBlock Text="{Binding MaybeNull, FallbackValue='N/A'}"/>
```

### 7. TextBlock vs Label

```xml
<!-- SLOW — Label is heavyweight -->
<Label Content="{Binding Name}"/>

<!-- FAST — TextBlock is lightweight -->
<TextBlock Text="{Binding Name}"/>
```

Use `Label` only when you need `_` underline access keys.

### 8. Background Thread for Data Loading

```csharp
public async Task LoadDataAsync()
{
    IsLoading = true;
    var data = await Task.Run(() => _service.LoadAll());
    
    _dispatcher.Invoke(() =>
    {
        Items.Clear();
        foreach (var item in data) Items.Add(item);
    });
    
    IsLoading = false;
}
```

Never block the UI thread.

### 9. BitmapCache for Complex Visuals

```xml
<Border>
    <Border.CacheMode>
        <BitmapCache RenderAtScale="1" SnapsToDevicePixels="True"/>
    </Border.CacheMode>
    <!-- Complex content -->
</Border>
```

Renders subtree once to bitmap, reuses it. Trades memory for speed.

### 10. Avoid Memory Leaks

**Common sources:**
- Event handlers not unsubscribed
- `DependencyPropertyDescriptor.AddValueChanged` without `RemoveValueChanged`
- Static events holding references

**Fix:**
```csharp
// LEAK
_model.PropertyChanged += OnPropertyChanged;

// SAFE — weak reference
PropertyChangedEventManager.AddHandler(_model, OnPropertyChanged, "");
```

---

## Layout Performance

**Panel Complexity (fastest to slowest):**
1. `Canvas` — absolute positioning
2. `StackPanel` — simple linear
3. `WrapPanel` — flowing content
4. `DockPanel` — edge-docked
5. `Grid` — complex row/column

Use the simplest panel that solves the problem.

**Build Trees Top-Down:**
```csharp
// SLOW — bottom-up (33× slower)
var child = new TextBlock();
var parent = new DockPanel();
parent.Children.Add(child);
canvas.Children.Add(parent);

// FAST — top-down
canvas.Children.Add(parent);
parent.Children.Add(child);
```

---

## RenderTransform vs LayoutTransform

```xml
<!-- LayoutTransform — triggers measure/arrange (SLOW for animations) -->
<Button>
    <Button.LayoutTransform>
        <RotateTransform Angle="45"/>
    </Button.LayoutTransform>
</Button>

<!-- RenderTransform — applied after layout (FAST for animations) -->
<Button>
    <Button.RenderTransform>
        <RotateTransform Angle="45"/>
    </Button.RenderTransform>
</Button>
```

**Always use `RenderTransform` for animations.**

---

## Binding Performance

**Dependency Property Resolution (fastest to slowest):**
1. Direct DP on `DependencyObject` — 90ms
2. INPC + Reflection on CLR object — 115ms
3. Reflection only (no INPC) — 115ms + overhead

**Always implement `INotifyPropertyChanged` on ViewModels.**

**Avoid 1000+ properties on one object.** Split into smaller objects.

---

## ComboBox Virtualization

```xml
<ComboBox IsEditable="True" ItemsSource="{Binding LargeList}">
    <ComboBox.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel/>
        </ItemsPanelTemplate>
    </ComboBox.ItemsPanel>
</ComboBox>
```

Default `ComboBox` doesn't virtualize in editable mode.
