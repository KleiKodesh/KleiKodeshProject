---
inclusion: fileMatch
fileMatchPattern: "**/*.xaml,**/*ViewModel*.cs,**/*viewmodel*.cs,**/*Viewmodel*.cs"
---

# Advanced WPF Binding

## RelativeSource Modes

```xml
<!-- Self — bind to same element -->
<Border Width="{Binding ActualHeight, RelativeSource={RelativeSource Self}}"/>

<!-- TemplatedParent — inside ControlTemplate -->
<Border Background="{Binding Background,
    RelativeSource={RelativeSource TemplatedParent}}"/>

<!-- FindAncestor — walk up visual tree -->
<TextBlock Foreground="{Binding Foreground,
    RelativeSource={RelativeSource AncestorType=UserControl}}"/>

<!-- PreviousData — in ItemsControl, bind to previous item -->
<TextBlock Text="{Binding RelativeSource={RelativeSource PreviousData}, Path=Name}"/>
```

**Performance Warning:** `FindAncestor` walks visual tree on every evaluation. For properties used in thousands of items, use inheritable attached properties instead.

---

## MultiBinding — Combine Multiple Sources

```csharp
public class FullNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string first && values[1] is string last)
            return $"{first} {last}";
        return "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

```xml
<TextBlock>
    <TextBlock.Text>
        <MultiBinding Converter="{StaticResource FullNameConverter}">
            <Binding Path="FirstName"/>
            <Binding Path="LastName"/>
        </MultiBinding>
    </TextBlock.Text>
</TextBlock>

<!-- Or use StringFormat for simple concatenation -->
<TextBlock>
    <TextBlock.Text>
        <MultiBinding StringFormat="{}{0} {1}">
            <Binding Path="FirstName"/>
            <Binding Path="LastName"/>
        </MultiBinding>
    </TextBlock.Text>
</TextBlock>
```

**Boolean logic:**
```csharp
public class AllTrueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is bool b && b);
}
```

```xml
<Button>
    <Button.IsEnabled>
        <MultiBinding Converter="{StaticResource AllTrue}">
            <Binding Path="HasSearchText"/>
            <Binding Path="IsNotBusy"/>
            <Binding Path="IsConnected"/>
        </MultiBinding>
    </Button.IsEnabled>
</Button>
```

---

## CollectionViewSource — Sort/Filter/Group

```xml
<!-- Define view in resources -->
<CollectionViewSource x:Key="ResultsView" Source="{Binding Results}">
    <CollectionViewSource.SortDescriptions>
        <scm:SortDescription PropertyName="Category"/>
        <scm:SortDescription PropertyName="Date"/>
    </CollectionViewSource.SortDescriptions>
    <CollectionViewSource.GroupDescriptions>
        <PropertyGroupDescription PropertyName="Category"/>
    </CollectionViewSource.GroupDescriptions>
</CollectionViewSource>

<!-- Bind to view -->
<ListBox ItemsSource="{Binding Source={StaticResource ResultsView}}"/>
```

**Dynamic filtering:**
```csharp
var view = CollectionViewSource.GetDefaultView(Results);
view.Filter = item => ((ResultItem)item).IsVisible;
```

**Master-detail:** Two controls bound to same `CollectionViewSource` share current item.

---

## Validation — INotifyDataErrorInfo

```csharp
public abstract class ValidatingViewModelBase : ViewModelBase, INotifyDataErrorInfo
{
    readonly Dictionary<string, List<string>> _errors = new();

    public bool HasErrors => _errors.Any(e => e.Value?.Count > 0);

    public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

    public IEnumerable GetErrors(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.SelectMany(e => e.Value);

        return _errors.TryGetValue(propertyName, out var errors)
            ? errors
            : Enumerable.Empty<string>();
    }

    protected void SetErrors(string propertyName, IEnumerable<string> errors)
    {
        var list = errors?.ToList() ?? new List<string>();

        if (list.Any())
            _errors[propertyName] = list;
        else
            _errors.Remove(propertyName);

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }
}
```

**Usage:**
```csharp
string _searchText;
public string SearchText
{
    get => _searchText;
    set
    {
        if (SetProperty(ref _searchText, value))
            ValidateSearchText();
    }
}

void ValidateSearchText()
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(_searchText))
        errors.Add("Search text cannot be empty");
    else if (_searchText.Length > 200)
        errors.Add("Search text is too long");
    
    SetErrors(nameof(SearchText), errors);
}
```

**XAML:**
```xml
<TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged,
                ValidatesOnNotifyDataErrors=True}"/>

<Style TargetType="TextBox">
    <Style.Triggers>
        <Trigger Property="Validation.HasError" Value="True">
            <Setter Property="BorderBrush" Value="#80FF4444"/>
            <Setter Property="ToolTip"
                    Value="{Binding (Validation.Errors)[0].ErrorContent,
                            RelativeSource={RelativeSource Self}}"/>
        </Trigger>
    </Style.Triggers>
</Style>
```

---

## TemplateBinding vs RelativeSource TemplatedParent

```xml
<!-- TemplateBinding — faster, compile-time, OneWay only -->
<Border Background="{TemplateBinding Background}"/>

<!-- RelativeSource TemplatedParent — TwoWay, complex paths -->
<TextBox Text="{Binding Text,
    RelativeSource={RelativeSource TemplatedParent},
    Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
```

Use `TemplateBinding` for simple one-way forwarding.  
Use `RelativeSource TemplatedParent` for TwoWay or complex paths.

---

## ElementName Binding

```xml
<!-- Slider controls TextBlock -->
<Slider x:Name="sizeSlider" Minimum="10" Maximum="40" Value="16"/>
<TextBlock FontSize="{Binding Value, ElementName=sizeSlider}" Text="Sample"/>
```

**Limitation:** Only works within same XAML namescope. Inside `DataTemplate`, `ElementName` cannot reach outside. Use `RelativeSource FindAncestor` or attached property instead.

---

## x:Static — Bind to Static Properties

```xml
<TextBlock Text="{x:Static local:AppConstants.Version}"/>
<Border Background="{x:Static SystemColors.WindowBrush}"/>
```

One-time lookup at XAML parse time. Does not update if static value changes.
