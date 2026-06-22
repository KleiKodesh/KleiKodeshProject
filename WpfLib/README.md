# WpfLib — Shared WPF Class Library

Shared WPF utilities used by all task pane libraries in KleiKodesh (DocDesignLib, RegexFindLib, WebSitesLib).

## What It Provides

| Subfolder | Contents | Used By |
|-----------|----------|---------|
| `ViewModels/` | Base MVVM classes (ViewModelBase, RelayCommand, TreeItemBase) | All WPF libs |
| `Helpers/` | Utilities (HebrewDateHelper, HebrewNumbering, FontsHelper, MsgBox) | All WPF libs |
| `Converters/` | WPF value converters (FlowDirection, Bool, String, Array/List) | All WPF libs |
| `Controls/` | Custom controls (UpDownTextBox, UpDownFloatTextBox) | DocDesignLib |
| `AttachedProperties/` | Attached behaviors (ButtonStrip, GridSetup, PopupListBox, TextBoxBehaviours) | All WPF libs |
| `Themes/` | Unified Office-theme style palette (OfficePalette.xaml merges Brushes, ScrollBar, ComboBox, Button, CheckBox styles) | All WPF libs |

## Key Files

### ViewModels/
- **`ViewModelBase.cs`** — Base with INotifyPropertyChanged via `SetProperty<T>(ref T, T)`. All ViewModels should inherit this.
- **`RelayCommand.cs`** — ICommand implementation with `Execute`/`CanExecute` delegates. Used for all command bindings.
- **`TreeItemBase.cs`** — Base for tree node VMs (IsExpanded, IsSelected, Children, Parent, Depth).
- **`CheckedTreeItemBase.cs`** — Extends TreeItemBase with `IsChecked` (nullable bool for three-state).

### Helpers/
- **`HebrewNumbering.cs`** — Converts integers to Hebrew numeral strings. Used by DocDesign for page numbering.
- **`HebrewDateHelper.cs`** — Hebrew calendar date calculations and formatting.
- **`FontsHelper.cs`** — Enumerates installed system fonts; font discovery.
- **`MsgBox.cs`** — Themed message box that respects Office theme. Used instead of raw MessageBox.
- **`ObservableCollectionExtensions.cs`** — `AddRange()`, `RemoveAll()` for ObservableCollection.
- **`EventArgs.cs`** — Generic `EventArgs<T>` for strongly-typed events.
- **`DependencyHelper.cs`** — Simple service locator / DI helper.
- **`ConfigurationManagerWrapper.cs`** — Reads app.config settings.

### Converters/
- **`FlowDirectionConverter.cs`** — RTL/LTR string → FlowDirection.
- **`BoolToFlowDirectionConverter.cs`** — bool → FlowDirection (true=RTL, false=LTR).
- **`ReverseBoolConverter.cs`** — Inverts boolean.
- **`StringToBoolConverter.cs`** — String content → bool (non-empty = true).
- **`ListToStringConverter.cs`** — IEnumerable → delimited string.
- **`ArrayToStringConverter.cs`** — Array → string.
- **`ToggleCheckBoxConverters.cs`** — Multi-converter for three-state CheckBox toggles.

### AttachedProperties/
- **`ButtonStripBehavior.cs`** — Groups buttons into a strip (removes sibling borders on hover).
- **`GridSetup.cs`** — Attached properties for grid row/column configuration from code.
- **`PopupListBoxBehaviour.cs`** — Attached behavior for popup-style list boxes.
- **`TextBoxBehaviours.cs`** — Attached behaviors: select-all-on-focus, watermark, numeric-only.

### Controls/
- **`UpDownTextBox.cs`** — Numeric TextBox with up/down spinner buttons. Integer mode.
- **`UpDownFloatTextBox.cs`** — Float variant of UpDownTextBox with configurable decimal places.

## Usage

All WPF libraries reference WpfLib via project reference. Import styles via:

```xml
<ResourceDictionary Source="/WpfLib;component/themes/officepalette.xaml"/>
```

Inherit ViewModelBase:
```csharp
public class MyViewModel : ViewModelBase { ... }
```

## Key Patterns

- **Adaptive mid-gray palette** — `Themes/Brushes.xaml` uses `#808080` overlays at various opacities instead of hardcoded light/dark colors, so one palette works on any Office theme.
- **VSTO-safe templates** — No `{StaticResource}` inside `<ControlTemplate>` bodies (prevents XamlParseException in HwndSource windows).
- **AddRange extension** — Always use `ObservableCollection.AddRange()` for bulk adds instead of loop-adding.
