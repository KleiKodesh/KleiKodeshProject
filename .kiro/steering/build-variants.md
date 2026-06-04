---
inclusion: fileMatch
fileMatchPattern: "Build/**"
---

# Build Variants — Three-Installer Pipeline

## The Three Variants

The build script (`Build/scripts/build-installer.ps1`) produces three installer variants by building `KleiKodeshVsto.csproj` with:

| Variant | Platform | VSTO OutputPath | Installer suffix |
|---------|----------|-----------------|-----------------|
| x64 | `Release\|x64` | `bin\Release-x64\` | `-x64` |
| x86 | `Release\|x86` | `bin\Release-x86\` | `-x86` |
| AnyCPU | `Release\|AnyCPU` | `bin\Release\` | _(none)_ |

## Current Dependency Chain

Every library in the chain must support all three variants:

```
WpfLib
RegexFindLib
DocDesignLib
UpdateCheckerLib
FtsLib
KitveiHakodeshLib  ←── KitveiHakodeshDemoApp (standalone host, also in chain)
KiwixLib
WebSitesLib
  └─→ KleiKodeshVsto
```

## Requirement for New Projects

**Any project added to the `KleiKodeshVsto` dependency chain must define all three Release platform configurations with the correct output paths.**

Required `PropertyGroup` blocks in every library `.csproj`:

```xml
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <PlatformTarget>AnyCPU</PlatformTarget>
  <DebugType>pdbonly</DebugType>
  <Optimize>true</Optimize>
  <OutputPath>bin\Release\</OutputPath>
  <DefineConstants>TRACE</DefineConstants>
  <ErrorReport>prompt</ErrorReport>
  <WarningLevel>4</WarningLevel>
</PropertyGroup>
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|x64' ">
  <PlatformTarget>AnyCPU</PlatformTarget>
  <DebugType>pdbonly</DebugType>
  <Optimize>true</Optimize>
  <OutputPath>bin\Release-x64\</OutputPath>
  <DefineConstants>TRACE</DefineConstants>
  <ErrorReport>prompt</ErrorReport>
  <WarningLevel>4</WarningLevel>
</PropertyGroup>
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|x86' ">
  <PlatformTarget>AnyCPU</PlatformTarget>
  <DebugType>pdbonly</DebugType>
  <Optimize>true</Optimize>
  <OutputPath>bin\Release-x86\</OutputPath>
  <DefineConstants>TRACE</DefineConstants>
  <ErrorReport>prompt</ErrorReport>
  <WarningLevel>4</WarningLevel>
</PropertyGroup>
```

**Key rules:**
- `OutputPath` must be `bin\Release\`, `bin\Release-x64\`, `bin\Release-x86\` — never the same path for all three.
- `PlatformTarget` is always `AnyCPU` — all libraries in this chain are pure managed code.
- Debug configs (`Debug|AnyCPU`, `Debug|x64`, `Debug|x86`) can all share `bin\Debug\`.

## Verification

To confirm a new library is correctly configured, build it standalone for all three variants:

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
foreach ($platform in @("AnyCPU", "x64", "x86")) {
    & $msbuild "path\to\MyLib.csproj" /p:Configuration=Release /p:Platform=$platform /nologo /verbosity:minimal
}
```

Each should output to a distinct `bin\Release*\` folder.

## Standalone Dev Tools — Exempt

Projects that are **not** in the `KleiKodeshVsto` dependency chain (demo apps, test harnesses) only need `Debug|AnyCPU` and `Release|AnyCPU`. Examples: `FtsLibDemo`, `FtsLibTest`, `DocDesignDemo`, `RegexFindDemo`, `KiwixDemoApp`, `WebSitesDemo`.

Note: `KitveiHakodeshDemoApp` **is** in the chain (it references `KitveiHakodeshLib` and is built as part of the VSTO output) so it requires all three variants.
