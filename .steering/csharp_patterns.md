---
inclusion: fileMatch
fileMatchPattern: '**/*.cs'
---

# C# Coding Patterns

## Modern C# Features
```csharp
// Target-typed new expressions
var list = new List<string>();  // Old
var list = new();               // Modern

// Using declarations
using var stream = File.OpenRead(path);  // Auto-dispose at scope end

// Expression-bodied members
public string Name => _name;
public void Log(string msg) => Console.WriteLine(msg);

// Pattern matching
if (obj is string str && str.Length > 0) { }
```

## JSON Serialization
```csharp
// Prefer System.Text.Json
using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    Converters = { new JsonStringEnumConverter() }
};

var obj = JsonSerializer.Deserialize<MyType>(json, options);
var json = JsonSerializer.Serialize(obj, options);
```

## Error Handling Pattern
```csharp
public void CommandHandler(JsonElement data)
{
    try
    {
        // Log received data for debugging
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        Debug.WriteLine($"Received: {json}");
        
        // Process with null-safe property getters
        var result = ProcessData(data);
        SendSuccessResponse(result);
    }
    catch (JsonException jex)
    {
        Debug.WriteLine($"JSON error: {jex.Message}");
        SendErrorResponse($"Invalid data: {jex.Message}");
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Processing error: {ex.Message}");
        SendErrorResponse($"Failed: {ex.Message}");
    }
}
```

## Async/Await Pattern
```csharp
// Async command handlers
private async void HandleCommand()
{
    try
    {
        var result = await SomeAsyncOperation();
        
        // Send response
        string js = $"window.receiveResponse && window.receiveResponse('{result}');";
        await _webView.ExecuteScriptAsync(js);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex}");
        string js = "window.receiveResponse && window.receiveResponse(null);";
        await _webView.ExecuteScriptAsync(js);
    }
}
```

## Resource Management
```csharp
// Using statements for IDisposable
using (var dialog = new OpenFileDialog())
{
    if (dialog.ShowDialog() == DialogResult.OK)
    {
        // Process file
    }
}

// Or with using declaration
using var dialog = new OpenFileDialog();
if (dialog.ShowDialog() == DialogResult.OK)
{
    // Process file
}
```