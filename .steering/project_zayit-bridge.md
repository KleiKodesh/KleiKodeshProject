---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Project C# ↔ Vue Bridge Pattern

## CRITICAL: Zayit-Specific Bridge Architecture

### Bridge System Overview
The Zayit project uses a centralized bridge system with these components:
- **C# Side**: `ZayitViewer.cs` with reflection-based command dispatch
- **Vue Side**: `CSharpBridge` class with promise-based request/response
- **Commands**: `ZayitViewerCommands.cs` with methods exposed via reflection

### MANDATORY Pattern for New Features

**✅ CORRECT Integration Steps:**

1. **Add C# Method** to `ZayitViewerCommands.cs`:
```csharp
/// <summary>
/// Method exposed to Vue via bridge (private methods work via reflection)
/// </summary>
private async void YourCommandName()
{
    try 
    {
        // Your logic here
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

2. **Add Bridge Handler** to `csharpBridge.ts`:
```typescript
// In setupGlobalHandlers()
win.receiveYourResponse = (data: any) => {
    const request = this.pendingRequests.get('YourCommandName')
    if (request) {
        request.resolve(data)
        this.pendingRequests.delete('YourCommandName')
    }
}
```

3. **Create Service** (optional but recommended):
```typescript
export class YourService {
    private bridge = new CSharpBridge()

    async performAction(): Promise<any> {
        const promise = this.bridge.createRequest('YourCommandName')
        this.bridge.send('YourCommandName', [])
        return await promise
    }
}
```

4. **Use in Vue Component**:
```typescript
import { yourService } from '../services/yourService'

const handleAction = async () => {
    try {
        const result = await yourService.performAction()
        // Handle result
    } catch (error) {
        // Handle error
    }
}
```

### CRITICAL: Response Pattern

**✅ ALWAYS use ExecuteScriptAsync for responses:**
```csharp
// CORRECT - calls global window function
string js = $"window.receiveYourData && window.receiveYourData('{data}');";
await _webView.ExecuteScriptAsync(js);
```

**❌ NEVER use PostWebMessage for responses:**
```csharp
// WRONG - breaks bridge pattern
_webView.CoreWebView2.PostWebMessageAsString(json);
```

### Command Naming Convention

**C# Method Names**: PascalCase (e.g., `OpenPdfFilePicker`, `GetTotalLines`)
**Bridge Commands**: Same PascalCase (e.g., `'OpenPdfFilePicker'`, `'GetTotalLines'`)
**Response Handlers**: `receive` + MethodName (e.g., `receivePdfFilePath`, `receiveTotalLines`)

### Parameter Handling

**No Parameters**:
```typescript
this.bridge.send('GetTree', [])  // Empty array
```

**With Parameters**:
```typescript
this.bridge.send('SearchLines', [bookId, searchTerm])
```

**C# Method Signature Matching**:
```csharp
// Method signature must match Args array length
private void SearchLines(int bookId, string searchTerm) // 2 parameters
```
```typescript
// Args array must have 2 elements
this.bridge.send('SearchLines', [bookId, searchTerm])
```

### Error Handling Pattern

**C# Side - Always Handle Exceptions**:
```csharp
private async void YourMethod()
{
    try 
    {
        var result = await YourOperation();
        string js = $"window.receiveResult && window.receiveResult('{result}');";
        await _webView.ExecuteScriptAsync(js);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Feature] Error: {ex}");
        // Always send null/error response
        string js = "window.receiveResult && window.receiveResult(null);";
        await _webView.ExecuteScriptAsync(js);
    }
}
```

**Vue Side - Service Pattern**:
```typescript
async performAction(): Promise<ResultType> {
    if (!this.bridge.isAvailable()) {
        throw new Error('C# bridge not available')
    }

    const promise = this.bridge.createRequest<ResultType>('YourCommand')
    this.bridge.send('YourCommand', [])
    
    const result = await promise
    if (!result) {
        throw new Error('Operation failed')
    }
    
    return result
}
```

### Initialization Pattern

**Add to ZayitViewer.cs initialization**:
```csharp
// In ZayitViewer_CoreWebView2InitializationCompleted
var commands = _commandHandler as ZayitViewerCommands;
_ = commands?.InitializeYourFeature(); // Fire-and-forget async
```

### PDF Manager Integration Example

**✅ CORRECT Implementation (Current)**:

1. **C# Method**: `OpenPdfFilePicker()` in `ZayitViewerCommands.cs`
2. **Response**: `receivePdfFilePath(filePath, fileName, dataUrl)` 
3. **Service**: `PdfService` with `showFilePicker()` method
4. **Virtual Host**: Uses `SetVirtualHostNameToFolderMapping` for direct file access

### Common Mistakes to Avoid

**❌ Wrong Response Method**:
```csharp
// Don't use PostWebMessage for bridge responses
CoreWebView2.PostWebMessageAsString(json);
```

**❌ Wrong Parameter Count**:
```typescript
// Method expects 2 params, sending 1
this.bridge.send('SearchLines', [bookId]) // Missing searchTerm
```

**❌ Missing Error Handling**:
```csharp
// Always wrap in try-catch and send response
private void YourMethod() {
    var result = SomeOperation(); // No error handling
    // No response sent
}
```

**❌ Inconsistent Naming**:
```csharp
private void openPdfPicker() // Wrong: camelCase
```
```typescript
this.bridge.send('openPdfPicker', []) // Wrong: doesn't match C# method
```

### Bridge System Benefits

- **Type Safety**: Promise-based with TypeScript interfaces
- **Error Handling**: Centralized error handling and timeouts
- **Consistency**: Same pattern for all C# ↔ Vue communication
- **Debugging**: Clear request/response logging
- **Fallbacks**: Easy to add mock implementations for development

### File Locations

- **C# Commands**: `vue-zayit/Zayit-cs/Zayit/Viewer/ZayitViewerCommands.cs`
- **Bridge Class**: `vue-zayit/zayit-vue/src/data/csharpBridge.ts`
- **Services**: `vue-zayit/zayit-vue/src/services/`
- **Components**: `vue-zayit/zayit-vue/src/components/`

This pattern ensures consistent, reliable communication between C# and Vue components in the Zayit project.