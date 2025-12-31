---
inclusion: fileMatch
fileMatchPattern: '**/WebView*|**/UserControl*|**/*Host*'
---

# VSTO WebView2 Integration

## Component Structure
```
KleiKodeshVsto/
├── [ComponentName]/
│   ├── [ComponentName]WebView.cs (extends WebViewBase)
│   ├── [ComponentName]UserControl.cs (wrapper)
│   ├── [ComponentName].cs (business logic)
│   └── [componentname]-index.html (built output)
```

## WebViewBase Auto-Discovery
Automatically discovers HTML files:
1. `[AssemblyDir]\[ComponentNamespace]\[componentname]-index.html`
2. Any `*.html` in `[AssemblyDir]\[ComponentNamespace]\`
3. Fallback: `[AssemblyDir]\Html\index.html`

## WebView Component Pattern
```csharp
public class ComponentNameWebView : WebViewBase
{
    private readonly ComponentName _component = new();

    public ComponentNameWebView()
    {
        SetCommandHandler(this);
    }

    public void PerformAction(ActionDto data)
    {
        try
        {
            var result = _component.ProcessAction(data);
            SendSuccessToVue(result);
        }
        catch (Exception ex)
        {
            SendErrorToVue($"Action failed: {ex.Message}");
        }
    }
}
```

## UserControl Wrapper
```csharp
public partial class ComponentNameUserControl : UserControl
{
    private ComponentNameWebView _webView;

    private void InitializeComponent()
    {
        _webView = new ComponentNameWebView();
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);
    }
}
```