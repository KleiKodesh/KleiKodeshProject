# ViewModels — MVVM Base Classes

Base classes for the MVVM pattern used by all WPF task pane libraries.

## Files

### `ViewModelBase.cs`
Base class implementing `INotifyPropertyChanged`. All ViewModels should inherit this.

Key members:
- `SetProperty<T>(ref T field, T value, [string propertyName])` — Sets field, raises PropertyChanged if value changed, returns bool
- `OnPropertyChanged(string propertyName)` — Raises PropertyChanged event
- `SetPropertyAsync<T>(ref T field, Task<T> valueTask)` — For async property initialization
- No dependency on any external MVVM toolkit — pure .NET

Usage:
```csharp
public class MyViewModel : ViewModelBase
{
    private string _myProperty;
    public string MyProperty
    {
        get => _myProperty;
        set => SetProperty(ref _myProperty, value);
    }
}
```

### `RelayCommand.cs`
`ICommand` implementation using delegates.

Key constructors:
- `RelayCommand(Action execute)` — Simple command
- `RelayCommand(Action execute, Func<bool> canExecute)` — Command with can-execute guard
- `RelayCommand<T>(Action<T> execute)` — Parameterized command

Key methods:
- `RaiseCanExecuteChanged()` — Re-evaluates `CanExecute`
- `Execute(object parameter)` — Delegates to the provided action
- `CanExecute(object parameter)` — Delegates to the provided predicate

### `TreeItemBase.cs`
Base class for tree node ViewModels. Used by WebSitesLib for site trees.

Key properties:
- `IsExpanded` (bool) — Expands/collapses node
- `IsSelected` (bool) — Selects node
- `Children` (ObservableCollection<TreeItemBase>) — Child nodes
- `Parent` (TreeItemBase) — Parent node reference
- `Depth` (int) — Nesting depth (computed from Parent chain)

### `CheckedTreeItemBase.cs`
Extends `TreeItemBase` with `IsChecked` (nullable bool) for three-state checkbox trees. Used in installer whitelist editor.

Key properties:
- `IsChecked` (bool?) — null = indeterminate (some children checked)
- Automatically propagates checked state to parent (all children checked → parent checked; none → unchecked; mixed → null) and children (checked → all children checked, etc.)
