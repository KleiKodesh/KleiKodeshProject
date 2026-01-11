---
inclusion: always
---

# Build Requirements

## VSTO Projects
- **AI Assistant Guidance**: When AI assists with builds, prefer `dotnet build` for modern .NET projects
- **VSTO Exception**: VSTO projects require Visual Studio MSBuild due to Office Tools dependencies
- **Project Files**: MSBuild targets in .csproj files should use the appropriate build tool for their target (MSBuild for VSTO, dotnet for modern .NET)
- **Build automation**: MSBuild targets handle HTML/Vue project builds automatically

## Modern C# Features
- **Target-typed new expressions**: Use `new()` instead of `new Type()`
- **Using declarations**: Use `using var` for automatic disposal
- **Expression-bodied members**: Use `=>` for simple methods and properties