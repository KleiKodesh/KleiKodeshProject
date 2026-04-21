# Routed Events

## Three Routing Strategies

```
Tunneling (Preview):  Root → ... → Source
                      PreviewKeyDown, PreviewMouseDown

Bubbling:             Source → ... → Root
                      KeyDown, MouseDown, Click

Direct:               Source only
                      MouseEnter, MouseLeave
```

**Preview + Bubbling pairs share same `EventArgs` instance.** Marking `PreviewKeyDown` as handled also handles `KeyDown`.

---

## Handler Invocation Order

For `KeyDown` on `TextBox` inside `StackPanel` inside `Window`:

```
1. Window.PreviewKeyDown          (tunneling — root first)
2. StackPanel.PreviewKeyDown
3. TextBox.PreviewKeyDown         (tunneling — source last)
4. TextBox.KeyDown                (bubbling — source first)
5. StackPanel.KeyDown
6. Window.KeyDown                 (bubbling — root last)
```

**Class handlers fire before instance handlers** at each node.

---

## Marking Events as Handled

```csharp
void OnKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter)
    {
        DoSomething();
        e.Handled = true;  // Stops further bubbling
    }
}
```

**When to mark handled:**
- Your handler provides complete response
- You want to prevent parent elements from responding

**When NOT to mark handled:**
- Logging or diagnostics
- When parent elements also need to respond

---

## Responding to Already-Handled Events

```csharp
// Must use AddHandler with handledEventsToo=true
element.AddHandler(
    Button.ClickEvent,
    new RoutedEventHandler(OnClick),
    handledEventsToo: true);  // Receives even handled events
```

Or handle `PreviewMouseLeftButtonDown` instead (tunneling fires before class handler suppresses).

---

## Creating Custom Routed Events

```csharp
public class MyControl : Control
{
    // 1. Register
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(
            "ValueChanged",
            RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>),
            typeof(MyControl));

    // 2. CLR wrapper
    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add    => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    // 3. Protected virtual raise method
    protected virtual void OnValueChanged(double oldValue, double newValue)
    {
        var args = new RoutedPropertyChangedEventArgs<double>(
            oldValue, newValue, ValueChangedEvent);
        RaiseEvent(args);
    }

    // 4. Raise from DP change callback
    static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MyControl)d).OnValueChanged((double)e.OldValue, (double)e.NewValue);
    }
}
```

**Why `RoutedEvent` instead of plain C# `event`:**
- Bubbles through visual tree
- Works with `EventTrigger` in XAML
- Works with `EventSetter` in styles
- Supports class handlers

---

## Class Handlers

Fire before instance handlers, registered in static constructor:

```csharp
static MyControl()
{
    EventManager.RegisterClassHandler(
        typeof(MyControl),
        Mouse.MouseDownEvent,
        new MouseButtonEventHandler(OnMouseDownClass));
}

static void OnMouseDownClass(object sender, MouseButtonEventArgs e)
{
    var ctrl = (MyControl)sender;
    ctrl.Focus();
}
```

**Prefer `override OnXxx` over `RegisterClassHandler`** for input events — simpler.

---

## Event Suppression in Composite Controls

`Button` suppresses `MouseLeftButtonDown` and raises `Click` instead:

```csharp
protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
{
    e.Handled = true;  // Suppress raw mouse event
    Focus();
    CaptureMouse();
    // ... eventually raises Click
}
```

Standard WPF pattern: replace low-level events with high-level semantic events.

---

## Weak Event Pattern

Prevents memory leaks when event source outlives listener:

```csharp
// LEAK — strong reference
_model.PropertyChanged += OnPropertyChanged;

// SAFE — weak reference
PropertyChangedEventManager.AddHandler(_model, OnPropertyChanged, "");

// Or generic
WeakEventManager<MyModel, PropertyChangedEventArgs>
    .AddHandler(_model, nameof(_model.PropertyChanged), OnPropertyChanged);
```

**When to use:**
- ViewModel subscribes to long-lived Model
- Static event subscriptions (always use weak)
- Both objects have same lifetime — plain `+=` is fine
