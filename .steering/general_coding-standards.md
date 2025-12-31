---
inclusion: always
---

# General Coding Standards

## Modern C# Features
- **Target-typed new expressions**: Use `new()` instead of `new Type()`
- **Using declarations**: Use `using var` for automatic disposal
- **Expression-bodied members**: Use `=>` for simple methods and properties
- **Pattern matching**: Use `is` patterns and switch expressions where appropriate

## Code Organization
- **Single Responsibility**: Each class should have one clear purpose
- **Minimal Classes**: Only include necessary properties and methods, remove unused code
- **Concise Code**: Prefer compact, readable code over verbose implementations
- **Clear naming**: Use descriptive names that explain intent

## Error Handling
- **Always catch exceptions** in command handlers and send error responses
- **Graceful degradation**: Provide fallbacks when operations fail
- **Clear error messages**: Include context about what failed and why
- **Resource cleanup**: Properly dispose of resources using `using` statements