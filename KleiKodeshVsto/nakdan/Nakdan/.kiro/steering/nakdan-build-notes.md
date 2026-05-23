# Nakdan Build Notes

## Office Interop Assembly Reference Warning

When building the Nakdan project with `dotnet build`, you may see this warning:

```
MSB3245: Could not resolve this reference. Could not locate the assembly 
"Microsoft.Office.Interop.Word, Version=15.0.0.0, Culture=neutral, 
PublicKeyToken=71e9bce111e9429c, processorArchitecture=MSIL"
```

**This is a red herring and can be safely ignored.**

### Why This Happens

The Office Interop assemblies are COM-based and don't resolve properly with `dotnet build` in modern .NET tooling. The project is configured to use MSBuild for compilation, which handles these references correctly.

### Solution

Use **MSBuild** instead of `dotnet build`:

```bash
msbuild "path\to\Nakdan.csproj"
```

Or build through Visual Studio, which uses MSBuild internally.

The warning does not affect the actual build output or functionality when using the correct build tool.
