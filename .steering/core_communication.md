---
inclusion: always
---

# C# ↔ JavaScript Communication

## CRITICAL: Property Naming Issues
JavaScript sends camelCase, C# expects PascalCase or specific property names.

**Common Issue**: JavaScript `searchText` → C# expects `Text` property

**Solution**: Use flexible property mapping
```csharp
// WRONG - assumes exact property match
public void Search(RegexFind options) // Fails with different property names

// CORRECT - handles property variations  
public void Search(JsonElement options) // Map properties manually
```

## JSON Handling Best Practices

### Null-Safe Property Getters
```csharp
private T GetProperty<T>(JsonElement element, params string[] propertyNames) where T : struct
{
    foreach (var name in propertyNames)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            // CRITICAL: Always check for null first
            if (prop.ValueKind == JsonValueKind.Null) continue;
            
            // Then try to get the value
            if (typeof(T) == typeof(float) && prop.TryGetSingle(out var floatVal))
                return (T)(object)floatVal;
        }
    }
    return default(T);
}
```

### Standard JsonSerializer Options
```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    Converters = { new JsonStringEnumConverter() }
};
```

## WebView2 Command Pattern
1. **JavaScript → C#**: `window.chrome.webview.postMessage(JSON.stringify(command))`
2. **C# Receives**: `CoreWebView2.WebMessageReceived` event
3. **Command Dispatch**: Reflection-based method invocation
4. **C# → JavaScript**: `CoreWebView2.PostWebMessageAsString(JSON.stringify(response))`