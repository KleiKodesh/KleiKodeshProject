# ViewModels

MVVM ViewModels for data binding and UI logic.

## Base Classes

- `ViewModelBase` — Base class for all ViewModels with INotifyPropertyChanged implementation

Use this as the parent class for all MVVM ViewModels in the project.

**Example:**
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
