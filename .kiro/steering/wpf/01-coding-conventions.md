# WPF Coding Conventions

## C# Naming

**Types:** `PascalCase` for classes, interfaces (`I` prefix), structs, enums, delegates, events  
**Members:** `PascalCase` for public/protected, `_camelCase` for private fields  
**Parameters/Locals:** `camelCase`

**Dependency Properties:**
```csharp
public static readonly DependencyProperty ValueProperty = ...;
public double Value { get; set; }  // Matches DP name without "Property"
private TextBox _textBox;          // Template part field
const string PartTextBox = "PART_TextBox";  // Template part name
```

**Routed Events:**
```csharp
public static readonly RoutedEvent ValueChangedEvent = ...;
public event RoutedPropertyChangedEventHandler<double> ValueChanged { ... }
```

**Why underscore for private fields?** Disambiguates in constructors:
```csharp
public MyViewModel(IService service)
{
    _service = service;  // Clear
}
```

---

## XAML Naming

**Elements:**
- Use `x:Name` (not `Name`) — works on all elements
- `PascalCase`: `x:Name="SearchButton"`
- Template parts: `x:Name="PART_TextBox"`
- Avoid Hungarian notation (`btnSearch`)

**Resources:**
- Prefix with category: `Brush_Primary`, `Style_HeaderText`
- Local resources: `Local_HeaderStyle`
- Descriptive names: `Converter_BoolToVisibility`

---

## XAML Formatting

**Attribute Layout:**
```xml
<!-- Multi-attribute: first on new line, closing bracket separate -->
<Button 
    Content="Search" 
    Command="{Binding SearchCommand}"
    Width="100"
    />

<!-- Single attribute: inline OK -->
<TextBlock Text="{Binding Title}"/>
```

**Attribute Order:**
1. `x:Name` / `x:Key`
2. Attached properties (`Grid.Row`)
3. Layout (`Width`, `Margin`)
4. Appearance (`Background`, `Foreground`)
5. Content/binding (`Text`, `ItemsSource`)
6. Behavior (`Command`, `IsEnabled`)

**Indentation:** 4 spaces, no tabs

---

## C# Code Style

**Braces:** Allman style (own line)
```csharp
if (condition)
{
    DoSomething();
}
```

**Line Length:** 120 characters max

**var Usage:**
```csharp
var text = "Hello";           // ✓ Type obvious
int result = ComputeValue();  // ✓ Type not obvious
```

**Strings:**
```csharp
var msg = $"Hello, {name}!";  // Interpolation
var sb = new StringBuilder(); // Loops
var json = """...""";         // Raw literals
```

**Async:**
```csharp
public async Task LoadAsync() { }      // ✓ Use Task
private async void OnLoaded(...) { }   // ✓ Event handlers only
```

---

## File Organization

**Namespace:**
```csharp
using System;
using System.Windows;

namespace MyApp.ViewModels;  // File-scoped

public class MyViewModel { }
```

**Class Member Order:**
1. Constants
2. Static fields (DPs, events)
3. Private fields
4. Static constructor
5. Constructors
6. Public properties
7. Protected/internal properties
8. Public methods
9. Protected/internal methods
10. Private methods
11. Event handlers

**Partial Classes:** Split by responsibility
```csharp
// MyViewModel.cs — state, properties
// MyViewModel.Commands.cs — command execution
```

---

## Resource Organization

**Structure:**
```
/Themes/
    BrushResources.xaml
    ConverterResources.xaml
    StyleResources.xaml
    ControlTemplates.xaml
```

**Naming:**
```xml
<SolidColorBrush x:Key="Brush_Primary" Color="#0078D4"/>
<Style x:Key="Style_HeaderText" TargetType="TextBlock">...</Style>
```

**Merge at App Level:**
```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/BrushResources.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

---

## WPF-Specific

**StaticResource vs DynamicResource:**
```xml
<Button Background="{StaticResource Brush_Primary}"/>  <!-- Default: 15-25% faster -->
<Border Background="{DynamicResource SystemBrush}"/>   <!-- Theme switching only -->
```

**SnapsToDevicePixels:** Always on root border
```xml
<Border SnapsToDevicePixels="True">...</Border>
```

**Avoid Code-Behind Logic:** Use MVVM commands instead
