---
inclusion: fileMatch
fileMatchPattern: '**/WebView*|**/*Dialog*'
---

# WebView2 Dialog Handling

## CRITICAL: Dialog Freezing Prevention

### Problem
WebView2 controls can freeze when showing Windows dialogs synchronously, especially file dialogs that remain open for extended periods.

### Root Cause
- **Blocking UI Thread**: Synchronous dialogs block the WebView2 message pump
- **Thread Conflicts**: WebView2 and Windows dialogs compete for UI thread resources
- **Parent Window Issues**: Incorrect dialog parenting causes focus conflicts

### Solution: WebViewDialogHelper

**✅ ALWAYS use WebViewDialogHelper for any dialogs from WebView2 context:**

```csharp
// File dialogs
string file = await WebViewDialogHelper.ShowOpenFileDialogAsync(webView, filter, title);
string saveFile = await WebViewDialogHelper.ShowSaveFileDialogAsync(webView, filter, title);
string folder = await WebViewDialogHelper.ShowFolderBrowserDialogAsync(webView, description);

// Custom dialogs
var result = await WebViewDialogHelper.ShowDialogAsync(webView, (parent) => {
    using (var dialog = new CustomDialog())
    {
        return parent != null ? dialog.ShowDialog(parent) : dialog.ShowDialog();
    }
});
```

### Key Features

1. **Async Handling**: Uses `TaskCompletionSource` for non-blocking operations
2. **Proper Threading**: Uses `BeginInvoke` instead of blocking `Invoke`
3. **Parent Window Management**: Automatically finds correct parent window
4. **Error Handling**: Robust exception handling and cleanup

### Implementation Pattern

**❌ WRONG - Synchronous Dialog:**
```csharp
private void ShowDialog()
{
    using (var dialog = new OpenFileDialog())
    {
        if (dialog.ShowDialog() == DialogResult.OK) // BLOCKS WebView2
        {
            // Process result
        }
    }
}
```

**✅ CORRECT - Async Dialog:**
```csharp
private async void ShowDialog()
{
    string file = await WebViewDialogHelper.ShowOpenFileDialogAsync(
        _webView, 
        "PDF Files (*.pdf)|*.pdf", 
        "Select File"
    );
    
    if (!string.IsNullOrEmpty(file))
    {
        // Process result
    }
}
```

### WebView Integration

When calling from WebView commands, ensure proper async handling:

```csharp
private async void OpenFileCommand()
{
    try
    {
        string file = await WebViewDialogHelper.ShowOpenFileDialogAsync(_webView, filter, title);
        
        if (!string.IsNullOrEmpty(file))
        {
            // Send result to JavaScript
            string js = $"window.receiveFile && window.receiveFile('{EscapeJs(file)}');";
            
            if (_webView.InvokeRequired)
            {
                _webView.BeginInvoke(new Action(async () => {
                    await _webView.ExecuteScriptAsync(js);
                }));
            }
            else
            {
                await _webView.ExecuteScriptAsync(js);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Dialog error: {ex}");
        // Send error response to JavaScript
    }
}
```

### Constructor Requirements

When creating dialog managers, pass both CoreWebView2 and WebView2 control:

```csharp
public class DialogManager
{
    private readonly CoreWebView2 _coreWebView;
    private readonly WebView2 _webViewControl;

    public DialogManager(CoreWebView2 coreWebView, WebView2 webViewControl)
    {
        _coreWebView = coreWebView;
        _webViewControl = webViewControl;
    }

    public async Task<string> ShowFileDialog()
    {
        return await WebViewDialogHelper.ShowOpenFileDialogAsync(_webViewControl, filter, title);
    }
}
```

### Error Handling

Always wrap dialog operations in try-catch and provide fallbacks:

```csharp
try
{
    string result = await WebViewDialogHelper.ShowOpenFileDialogAsync(webView, filter, title);
    return result;
}
catch (Exception ex)
{
    Console.WriteLine($"Dialog error: {ex}");
    
    // Fallback to synchronous dialog if async fails
    using (var dialog = new OpenFileDialog())
    {
        dialog.Filter = filter;
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
```

### Testing

Test dialog behavior with:
1. **Extended Open Time**: Leave dialogs open for 30+ seconds
2. **Multiple Dialogs**: Open multiple dialogs in sequence
3. **WebView Interaction**: Interact with WebView while dialog is open
4. **High DPI Displays**: Test on various DPI settings

### Common Issues Fixed

- **HebrewBooksDownloadManager**: SaveFileDialog now uses async helper
- **CSharpPdfManager**: OpenFileDialog uses async helper
- **All Future Dialogs**: Must use WebViewDialogHelper pattern

This pattern ensures all dialogs work smoothly with WebView2 without freezing or blocking the UI.