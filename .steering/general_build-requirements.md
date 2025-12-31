---
inclusion: always
---

# Build Requirements

## VSTO Projects
- **Always use `dotnet build`** - never msbuild directly
- **Cross-platform compatibility**: dotnet build provides better dependency resolution
- **Build automation**: MSBuild targets handle HTML/Vue project builds automatically

## Modern C# Features
- **Target-typed new expressions**: Use `new()` instead of `new Type()`
- **Using declarations**: Use `using var` for automatic disposal
- **Expression-bodied members**: Use `=>` for simple methods and properties