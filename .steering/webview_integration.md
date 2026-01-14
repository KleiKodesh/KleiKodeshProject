---
inclusion: fileMatch
fileMatchPattern: '**/WebView*|**/UserControl*|**/*Host*'
---

# WebView2 Integration Patterns

## Component Structure
```
KleiKodeshVsto/
├── [ComponentName]/
│   ├── [ComponentName]Host.cs (UserControl wrapper)
│   ├── [ComponentName].cs (business logic)
│   └── [componentname]-index.html (built output)
```

## KleiKodeshWebView Base Class
```csharp
public class ComponentNameHost : UserControl
{
    private KleiKodeshWebView _webView;

    public ComponentNameHost()
    {
        string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
            "ComponentName", "index.html");
        
        _webView = new KleiKodeshWebView(this, htmlPath);
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);
    }

    // Command handlers called from HTML app
    public void YourCommand(JsonElement data)
    {
        try
        {
            // Process command
            var result = ProcessData(data);
            SendDataToHtml("commandResponse", result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command failed: {ex.Message}");
        }
    }
}
```

## Virtual Host Mapping
```csharp
// Automatic virtual host setup in KleiKodeshWebView
string folderPath = Path.GetDirectoryName(_htmlFilePath);
CoreWebView2.SetVirtualHostNameToFolderMapping(
    "appHost", 
    folderPath, 
    CoreWebView2HostResourceAccessKind.Allow
);
```

## Command Dispatch Pattern
1. **JavaScript → C#**: `window.chrome.webview.postMessage(JSON.stringify(command))`
2. **C# Receives**: `CoreWebView2.WebMessageReceived` event
3. **Reflection Dispatch**: Finds method by command name, invokes with JsonElement
4. **C# → JavaScript**: `CoreWebView2.PostWebMessageAsString(JSON.stringify(response))`

## Shared Environment
- Single `CoreWebView2Environment` shared across all WebView instances
- Cached in `_sharedEnvironment` static field
- User data folder: `%LocalAppData%\WebView2SharedCache`
- Improves performance and memory usage