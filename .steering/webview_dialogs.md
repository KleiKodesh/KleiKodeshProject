---
inclusion: fileMatch
fileMatchPattern: '**/WebView*|**/*Dialog*'
---

# WebView2 Dialog Handling

## CRITICAL: Dialog Freezing Prevention
WebView2 controls freeze when showing Windows dialogs synchronously.

## Solution: WebViewDialogHelper
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

## Why This Works
1. **Async Handling**: Uses `TaskCompletionSource` for non-blocking operations
2. **Proper Threading**: Uses `BeginInvoke` instead of blocking `Invoke`
3. **Parent Window Management**: Automatically finds correct parent window
4. **Error Handling**: Robust exception handling and cleanup

## WebView Integration Pattern
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
            await _webView.ExecuteScriptAsync(js);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Dialog error: {ex}");
        // Send error response to JavaScript
    }
}
```

## Common Issues Fixed
- **HebrewBooksDownloadManager**: SaveFileDialog uses async helper
- **CSharpPdfManager**: OpenFileDialog uses async helper
- **All Future Dialogs**: Must use WebViewDialogHelper pattern

## Testing Checklist
- [ ] Leave dialogs open for 30+ seconds
- [ ] Open multiple dialogs in sequence
- [ ] Interact with WebView while dialog is open
- [ ] Test on high DPI displays