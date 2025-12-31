---
inclusion: fileMatch
fileMatchPattern: '**/*.cs|**/*.js|**/*.vue'
---

# JSON Handling Best Practices

## CRITICAL: Null Value Handling

### Common JsonException Pattern
**PROBLEM**: JSON contains null values causing deserialization failures.

**SYMPTOMS**:
- `System.Text.Json.JsonException` in debug output
- Properties with null values fail to deserialize
- Unicode text processing fails unexpectedly

### Root Cause
JavaScript/Vue can send `null` values in JSON:
```json
{
  "fontSize": null,
  "color": null,
  "someProperty": null
}
```

C# `JsonElement.TryGetSingle()` and similar methods throw exceptions on null values.

## SOLUTION: Null-Safe Property Getters

### Template for All Property Getters
```csharp
private T GetProperty<T>(JsonElement element, params string[] propertyNames) where T : struct
{
    foreach (var name in propertyNames)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            // CRITICAL: Always check for null first
            if (prop.ValueKind == JsonValueKind.Null)
                continue;
                
            // Then try to get the value
            if (typeof(T) == typeof(float) && prop.TryGetSingle(out var floatVal))
                return (T)(object)floatVal;
            if (typeof(T) == typeof(int) && prop.TryGetInt32(out var intVal))
                return (T)(object)intVal;
            if (typeof(T) == typeof(bool) && prop.TryGetBoolean(out var boolVal))
                return (T)(object)boolVal;
        }
    }
    return default(T);
}

private string GetStringProperty(JsonElement element, params string[] propertyNames)
{
    foreach (var name in propertyNames)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            // Handle null values
            if (prop.ValueKind == JsonValueKind.Null)
                continue;
                
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
    }
    return null;
}
```

## JsonSerializer Configuration

### Standard Options for WebView Communication
```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    Converters = { new JsonStringEnumConverter() }
};
```

### Why These Options Matter
- `PropertyNameCaseInsensitive`: Handles camelCase ↔ PascalCase conversion
- `DefaultIgnoreCondition.WhenWritingNull`: Prevents null values in outgoing JSON
- `NumberHandling.AllowReadingFromString`: Handles string numbers from HTML forms
- `JsonStringEnumConverter`: Handles enum serialization properly

## Error Handling Pattern

### Method-Level Error Handling
```csharp
public void ProcessJsonCommand(JsonElement data)
{
    try
    {
        // Always log received JSON for debugging
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        System.Diagnostics.Debug.WriteLine($"Received JSON: {json}");
        
        // Process with null-safe property getters
        var result = ProcessData(data);
        
        SendSuccessResponse(result);
    }
    catch (JsonException jex)
    {
        System.Diagnostics.Debug.WriteLine($"JSON processing error: {jex.Message}");
        SendErrorResponse($"Invalid data format: {jex.Message}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Processing error: {ex.Message}");
        SendErrorResponse($"Processing failed: {ex.Message}");
    }
}
```

## Testing with Edge Cases

### Always Test These Scenarios
1. **Null values**: `{ "property": null }`
2. **Missing properties**: `{ "otherProperty": "value" }`
3. **Unicode text**: Hebrew (כדי), Arabic (مرحبا), Chinese (你好)
4. **Number formats**: `"12"`, `12`, `12.5`, `null`
5. **Empty strings**: `""`, `null`

### Test Data Template
```json
{
  "text": "כדי",
  "fontSize": null,
  "count": "12",
  "enabled": true,
  "missing": null,
  "nested": {
    "value": null,
    "text": ""
  }
}
```

## Prevention Checklist

### Before Implementing JSON Communication
- [ ] Use null-safe property getters
- [ ] Configure JsonSerializer with robust options
- [ ] Add comprehensive error handling
- [ ] Test with null values and Unicode text
- [ ] Log received JSON for debugging
- [ ] Handle both camelCase and PascalCase properties

### Code Review Checklist
- [ ] All `TryGet*` calls check for `JsonValueKind.Null` first
- [ ] JsonSerializer uses recommended options
- [ ] Error handling catches and logs JsonException
- [ ] Debug output shows received JSON structure
- [ ] Unicode text handling is tested

This prevents the JsonException pattern that caused RegexFind search failures with Hebrew text.