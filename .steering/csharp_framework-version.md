---
inclusion: fileMatch
fileMatchPattern: '**/*.csproj'
---

# C# .NET Framework Version Standard

## CRITICAL: Framework Version Consistency

**ALL C# projects in this solution MUST use .NET Framework 4.8**

## Why .NET Framework 4.8

- **VSTO Compatibility**: Office VSTO add-ins require .NET Framework (not .NET Core/.NET 5+)
- **Windows Desktop**: All projects target Windows desktop applications
- **Consistency**: Single framework version prevents compatibility issues
- **Latest Framework**: 4.8 is the final and most stable .NET Framework version
- **Office Integration**: Required for Word interop and Office APIs

## Project Configuration

Every `.csproj` file must specify:

```xml
<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
```

## Projects Using .NET Framework 4.8

- KleiKodeshVsto (main VSTO add-in)
- KleiKodeshVstoInstallerWpf (installer)
- DocSeferLib
- UpdateCheckerLib
- WpfLib
- WebViewLib
- WebSitesLib
- ZayitLib
- ZayitWrapper
- LuceneIndexer
- MinimalIndexer
- All test projects

## When Adding New Projects

1. Always set `TargetFrameworkVersion` to `v4.8`
2. Use traditional .csproj format (not SDK-style) for consistency with VSTO
3. Verify compatibility with Office interop assemblies

## Migration Notes

- Converted from .NET 8.0 to .NET Framework 4.8 (February 2026)
- All projects standardized to v4.8 from mixed v4.7.2/v4.8
- SDK-style projects converted to traditional format where needed

## DO NOT

- ❌ Use .NET Core, .NET 5, .NET 6, .NET 7, or .NET 8
- ❌ Mix framework versions across projects
- ❌ Use SDK-style project format for VSTO projects
- ❌ Target .NET Standard (use .NET Framework 4.8 directly)
