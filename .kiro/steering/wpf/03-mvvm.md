---
inclusion: fileMatch
fileMatchPattern: "**/*.xaml,**/*ViewModel*.cs,**/*viewmodel*.cs,**/*Viewmodel*.cs"
---

# WPF MVVM Pattern

## The Three Layers

```
VIEW (.xaml + minimal .xaml.cs)
  • DataContext = ViewModel instance
  • Binds to VM properties, commands, collections
  • Zero business logic

VIEWMODEL (: ViewModelBase / INotifyPropertyChanged)
  • All state as properties with INPC
  • All user actions as ICommand
  • No System.Windows references
  • Calls Model/services, never touches UI

MODEL (plain C# classes + services)
  • Pure data and business logic
  • No INPC, no ICommand, no WPF
```

---

## ViewModelBase

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value,
        [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;  // Returns true if changed
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

---

## Property Patterns

**Simple Property:**
```csharp
string _searchText = "";
public string SearchText
{
    get => _searchText;
    set => SetProperty(ref _searchText, value);
}
```

**Property with Side Effects:**
```csharp
int _selectedIndex = -1;
public int SelectedIndex
{
    get => _selectedIndex;
    set
    {
        if (SetProperty(ref _selectedIndex, value) && value >= 0)
            _service.SelectItem(value);  // Only when actually changed
    }
}
```

**Computed Property:**
```csharp
bool _useRegex;
public bool UseRegex
{
    get => _useRegex;
    set
    {
        if (SetProperty(ref _useRegex, value))
            OnPropertyChanged(nameof(CanSearch));  // Notify dependent
    }
}

public bool CanSearch => !string.IsNullOrEmpty(_searchText);  // No setter
```

**Read-Only Collection:**
```csharp
public ObservableCollection<Item> Results { get; } = new();
```

**Static Shared Collection:**
```csharp
public static readonly ObservableCollection<FontItem> FontList = new();
public ObservableCollection<FontItem> FontListBinding => FontList;  // Instance proxy
```

---

## Commands — RelayCommand

```csharp
public class RelayCommand : ICommand
{
    readonly Action<object> _execute;
    readonly Func<object, bool> _canExecute;

    public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool> canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public event EventHandler CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
```

**Initialize Commands:**
```csharp
void InitCommands()
{
    SearchCommand = new RelayCommand(
        execute:    ExecuteSearch,
        canExecute: () => !string.IsNullOrEmpty(SearchText));

    ReplaceCommand = new RelayCommand(
        execute:    ExecuteReplace,
        canExecute: () => !string.IsNullOrEmpty(SearchText) && !string.IsNullOrEmpty(ReplaceText));
}
```

**Bind in XAML:**
```xml
<Button Content="Search" Command="{Binding SearchCommand}"/>
<Button Command="{Binding DeleteCommand}" CommandParameter="{Binding SelectedItem}"/>

<UserControl.InputBindings>
    <KeyBinding Key="Return" Command="{Binding SearchCommand}"/>
</UserControl.InputBindings>
```

---

## DataContext Wiring

**In Code-Behind (VSTO/ElementHost):**
```csharp
// UserControl constructor
public RegexFindView()
{
    InitializeComponent();
    // DataContext set by host, not here
}

// In TaskpaneManager
var vm = new RegexFindViewModel(wordService);
var view = new RegexFindView { DataContext = vm };
```

**Never create ViewModel in View constructor** — breaks testing.

---

## Binding Modes

| Mode | Direction | Use |
|------|-----------|-----|
| `OneWay` | Source → Target | Display-only |
| `TwoWay` | Source ↔ Target | Editable inputs |
| `OneTime` | Once at load | Static data |
| `Default` | Depends on DP | **Use this** |

```xml
<!-- Default picks TwoWay for TextBox.Text -->
<TextBox Text="{Binding SearchText}"/>

<!-- Live search — update on every keystroke -->
<TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"/>
```

---

## ItemsControl Binding

```xml
<ListBox ItemsSource="{Binding Results}"
         SelectedIndex="{Binding SelectedResultIndex}"
         VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Recycling">
    
    <ListBox.ItemTemplate>
        <DataTemplate DataType="{x:Type local:SnippetModel}">
            <StackPanel>
                <TextBlock Text="{Binding MatchText}" FontWeight="Bold"/>
                <TextBlock Text="{Binding Context}" Opacity="0.7"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
    
    <ListBox.ItemContainerStyle>
        <Style TargetType="ListBoxItem">
            <Setter Property="Padding" Value="6,4"/>
        </Style>
    </ListBox.ItemContainerStyle>
</ListBox>
```

---

## Async in ViewModels

```csharp
public partial class MyViewModel : ViewModelBase
{
    // Capture dispatcher at construction (VSTO: Application.Current is null)
    readonly Dispatcher _dispatcher =
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var data = await Task.Run(() => _service.LoadAll());
            
            // Marshal to UI thread
            _dispatcher.Invoke(() =>
            {
                Items.Clear();
                foreach (var item in data) Items.Add(item);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

**Rules:**
- Capture `_dispatcher` in constructor
- `ObservableCollection` must be modified on UI thread
- Use `async Task`, not `async void` (except event handlers)

---

## What Belongs Where

| Thing | Where | Why |
|-------|-------|-----|
| User-visible text | ViewModel property | Bindable, testable |
| Button enabled/disabled | `CanExecute` on `ICommand` | Declarative |
| List of items | `ObservableCollection<T>` | Auto-notifies WPF |
| Selected item index | `int` property | Bindable to `SelectedIndex` |
| Loading spinner | `bool IsLoading` property | Converter → Visibility |
| Error message | `string ErrorText` property | Bindable to TextBlock |
| Color/brush | `bool IsError` + converter | VM stays WPF-free |
| Keyboard shortcut | `InputBinding` in XAML | Bound to VM command |
| Focus management | Code-behind | Pure UI concern |
| Animation | Code-behind/triggers | Pure UI concern |
| Window close | Code-behind event | Lifecycle event |
| Calling service | ViewModel method | Business logic |

---

## Code-Behind — What's Acceptable

**Acceptable:**
- Lifecycle events (`Loaded`, `Closing`, `SizeChanged`)
- Keyboard shortcuts
- Focus routing
- Animations tightly coupled to UI

**Not Acceptable:**
- Business logic
- Direct control manipulation (`MyTextBox.Text = ...`)
- Event handling that should be commands

```csharp
// WRONG
private void OnButtonClick(object sender, RoutedEventArgs e)
{
    var result = _service.ComputeValue();
    ResultTextBlock.Text = result.ToString();
}

// CORRECT
// XAML: <Button Command="{Binding ComputeCommand}"/>
// ViewModel:
public ICommand ComputeCommand { get; }
private void ExecuteCompute()
{
    Result = _service.ComputeValue().ToString();
}
```

---

## Partial Classes for Large ViewModels

```csharp
// MyViewModel.cs — state, properties, constructor
public partial class MyViewModel : ViewModelBase
{
    private string _searchText;
    public string SearchText { get; set; }
    
    public MyViewModel() { InitCommands(); }
}

// MyViewModel.Commands.cs — command execution
public partial class MyViewModel
{
    private void ExecuteSearch() { }
    private void ExecuteReplace() { }
}
```

Keep each file under 200 lines.
