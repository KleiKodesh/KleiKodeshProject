---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Project Architecture

## Component Overview
- **Vue Frontend**: Modern Vue 3 + TypeScript interface for Hebrew text browsing
- **C# Backend**: SQLite database integration with PDF.js viewer
- **Bridge System**: Promise-based C# ↔ Vue communication
- **PDF Integration**: Custom PDF.js with Hebrew locale and theme sync

## Bridge Communication Pattern

### C# → Vue Response Pattern
```csharp
// Method exposed via reflection in ZayitViewerCommands.cs
private async void YourCommandName()
{
    try 
    {
        var result = await SomeOperation();
        
        // Send response via ExecuteScriptAsync (NOT PostWebMessage)
        string js = $"window.receiveYourResponse && window.receiveYourResponse('{result}');";
        await _webView.ExecuteScriptAsync(js);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[YourFeature] Error: {ex}");
        string js = "window.receiveYourResponse && window.receiveYourResponse(null);";
        await _webView.ExecuteScriptAsync(js);
    }
}
```

### Vue → C# Request Pattern
```typescript
// In csharpBridge.ts
win.receiveYourResponse = (data: any) => {
    const request = this.pendingRequests.get('YourCommandName')
    if (request) {
        request.resolve(data)
        this.pendingRequests.delete('YourCommandName')
    }
}

// Service usage
const promise = this.bridge.createRequest('YourCommandName')
this.bridge.send('YourCommandName', [])
return await promise
```

## Key Features

### Multi-Tab Interface
- Tab store (Pinia) manages navigation state
- KeepAlive caching preserves component state
- Session persistence across app restarts

### Hebrew Text Support
- **Diacritics Control**: Full text, no cantillation, no diacritics
- **Divine Name Censoring**: יהוה → ק replacement
- **RTL Layout**: Full right-to-left support
- **Font Customization**: Separate headers/body fonts

### PDF System
- **Local PDFs**: File dialog → virtual host mapping
- **Hebrew Books**: hebrewbooks.org integration with cache
- **PDF.js Integration**: Custom Hebrew locale with theme sync

## Architecture Pattern
**Container/Presentational with Centralized Routing**:
- **Tab Store**: Application-level navigation coordination
- **Smart Components**: Self-contained with local UI state
- **Functional Composition**: Independent components with store coordination