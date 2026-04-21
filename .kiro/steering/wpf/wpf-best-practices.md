# WPF Best Practices — Index

This guide has been split into focused topics for clarity. Each file covers a specific aspect of WPF development.

## Files

### [01-coding-conventions.md](wpf/01-coding-conventions.md)
C# and XAML naming conventions, code style, file organization, resource organization.

**Key Topics:**
- Naming: `PascalCase`, `_camelCase`, dependency properties, routed events
- XAML formatting: attribute layout, indentation, attribute order
- C# style: braces, var usage, strings, async
- File organization: namespaces, class member order, partial classes
- Resource organization: structure, naming, merging

### [02-custom-controls.md](wpf/02-custom-controls.md)
Building custom WPF controls with dependency properties, routed events, and templates.

**Key Topics:**
- Control vs UserControl
- Custom control pattern for ElementHost/VSTO
- Dependency properties: complete pattern, read-only DPs
- Routed events: registration, CLR wrappers
- OnApplyTemplate: template part contract
- Template best practices: disabled state, SnapsToDevicePixels, ContentPresenter

### [03-mvvm.md](wpf/03-mvvm.md)
Model-View-ViewModel pattern implementation.

**Key Topics:**
- Three layers: View, ViewModel, Model
- ViewModelBase: INotifyPropertyChanged, SetProperty
- Property patterns: simple, side effects, computed, collections
- Commands: RelayCommand implementation
- DataContext wiring
- Binding modes and ItemsControl binding
- Async in ViewModels
- What belongs where

### [04-performance.md](wpf/04-performance.md)
Performance optimization techniques.

**Key Topics:**
- Virtualization for ItemsControls
- StaticResource vs DynamicResource
- Freeze Freezables
- Opacity on Brush vs Element
- Fix binding errors
- TextBlock vs Label
- Background thread for data loading
- Layout performance: panel complexity, build trees top-down
- RenderTransform vs LayoutTransform

### [05-elementhost-vsto.md](wpf/05-elementhost-vsto.md)
WPF in ElementHost and VSTO environments.

**Key Topics:**
- Theme-aware colors: mid-gray opacity overlays
- **StaticResource inside ControlTemplates: NEVER — inline all values (crashes in Window/HwndSource scope)**
- Popup background in separate HwndSource
- Dispatcher in VSTO (Application.Current is null)
- Generic.xaml not loaded
- Theme inheritance
- DataContext binding pattern

### [06-binding-advanced.md](wpf/06-binding-advanced.md)
Advanced binding techniques.

**Key Topics:**
- RelativeSource modes: Self, TemplatedParent, FindAncestor, PreviousData
- MultiBinding: combine multiple sources
- CollectionViewSource: sort, filter, group
- Validation: INotifyDataErrorInfo
- TemplateBinding vs RelativeSource TemplatedParent
- ElementName binding
- x:Static for static properties

### [07-attached-properties.md](wpf/07-attached-properties.md)
Attached properties and behaviors.

**Key Topics:**
- Attached property pattern: RegisterAttached, Get/Set accessors
- Inheritable attached properties
- Common attached behaviors: SelectAllOnFocus
- Click callbacks from DataTemplate
- Built-in attached properties: Grid, DockPanel, Canvas

### [08-routed-events.md](wpf/08-routed-events.md)
Routed event system.

**Key Topics:**
- Three routing strategies: Tunneling, Bubbling, Direct
- Handler invocation order
- Marking events as handled
- Responding to already-handled events
- Creating custom routed events
- Class handlers
- Event suppression in composite controls
- Weak event pattern for memory leak prevention

### [09-debugging.md](wpf/09-debugging.md)
Debugging WPF applications.

**Key Topics:**
- Finding binding errors: PresentationTraceSources
- Trace specific binding
- Inspecting visual tree at runtime
- Checking property value source
- Common binding errors and fixes
- Snoop tool
- Visual tree vs Logical tree
- Dependency property value precedence

---

## Quick Reference

**Most Important Rules:**
1. Use `Control` with `ControlTemplate` for reusable controls, `UserControl` only for one-off views
2. Always implement `INotifyPropertyChanged` on ViewModels
3. Virtualize ItemsControls with large data sets
4. Use `StaticResource` by default, `DynamicResource` only for theme switching
5. Never hardcode light-mode colors in ElementHost — use mid-gray opacity overlays
6. Capture dispatcher in constructor before async operations (VSTO)
7. Use `_camelCase` for private fields, `PascalCase` for public members
8. CLR wrappers for DPs must contain ONLY `GetValue`/`SetValue`
9. Always unwire old event handlers before wiring new ones in `OnApplyTemplate`
10. Use `RenderTransform` for animations, not `LayoutTransform`

**Common Patterns:**
- Dependency Property: `ValueProperty` (static readonly) + `Value` (CLR wrapper)
- Routed Event: `ValueChangedEvent` (static readonly) + `ValueChanged` (CLR wrapper)
- Template Part: `const string PartTextBox = "PART_TextBox"` + `[TemplatePart]` attribute
- Private Field: `_textBox`, `_isLoading`, `_dispatcher`
- Resource Key: `Brush_Primary`, `Style_HeaderText`, `Local_ContentText`

**Project-Specific (ElementHost/VSTO):**
- `Application.Current` is null — capture dispatcher in constructor
- `Generic.xaml` not loaded — merge ResourceDictionaries explicitly
- Use mid-gray opacity overlays for theme-aware colors
- Bind Popup backgrounds to `TemplatedParent`
- **Never use `{StaticResource ...}` inside a `ControlTemplate` body** — inline literal values instead (`#50808080` not `{StaticResource BorderBrush}`). ControlTemplates defined in merged dictionaries are instantiated in the element's own resource scope; if that element lives in a `Window` or separate `HwndSource`, named resources from the merged dictionary are not visible and cause `XamlParseException` at runtime.
