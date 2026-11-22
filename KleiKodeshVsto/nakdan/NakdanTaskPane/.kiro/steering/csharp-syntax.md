# C# 7.3 Syntax Guidelines

This project targets C# 7.3 and .NET Framework 4.8. Use only language features available in C# 7.3.

## Syntax Rules

- **No pattern matching with `is not`**: Use `!(x is Type)` instead of `x is not Type` (pattern matching with `not` requires C# 9.0+)
- **No nullable reference types**: The `?` operator on reference types is not available
- **No records**: Use classes instead
- **No init-only properties**: Use regular properties with private setters
- **No top-level statements**: All code must be in classes/methods
- **No file-scoped types**: Use namespace declarations

## Approved C# 7.3 Features

- Tuple deconstruction: `var (x, y) = tuple;`
- Expression-bodied members: `public string Name => _name;`
- In parameters: `void Method(in int value)`
- Ref locals and returns: `ref int GetRef()`
- Stackalloc: `Span<int> data = stackalloc int[10];`
- Generic constraints: `where T : unmanaged`

## Type Checking Pattern

For type checks, use this pattern:
```csharp
if (obj is JsonObject jsonObj)
{
    // Use jsonObj
}
else
{
    // Handle non-match
}
```

NOT:
```csharp
if (obj is not JsonObject)  // C# 9.0+ syntax
```
