---
inclusion: fileMatch
fileMatchPattern: "Build/**"
---

# Build — One Installer (AnyCPU)

## Since 2026-10-11 there is ONE installer

The build script (`Build/scripts/build-installer.ps1`) builds `KleiKodeshVsto.csproj` as
`Release|AnyCPU` (output `bin\Release\`) and produces one installer:

```
KleiKodeshSetup-{version}.exe
```

The x64 and x86 installers (`-x64` / `-x86` suffixes, `bin\Release-x64\` / `bin\Release-x86\`)
are gone. Do not reintroduce them:

- The Word add-in and its libraries are pure managed code, AnyCPU, and load in 32-bit or
  64-bit Office alike. The per-bitness variants existed for the native SQLite interop DLLs,
  and the add-in no longer ships any SQLite (all data goes through the KitveiHakodesh service).
- The KitveiHakodesh service is a separate native-AOT **x64** process, shipped in the one
  installer (`KitveiHakodeshService\`). 32-bit Windows is not supported.
- The installer stamps `HKCU\SOFTWARE\KleiKodesh\InstallerVariant = AnyCPU` and the service's
  updater downloads the unsuffixed release asset.

## Rules for projects in the add-in chain

```
WpfLib  RegexFindLib  DocDesignLib  KiwixLib  WebSitesLib
KitveiHakodeshLib  ←── KitveiHakodeshDemoApp
  └─→ KleiKodeshVsto
```

- `Debug|AnyCPU` → `bin\Debug\`, `Release|AnyCPU` → `bin\Release\`.
- `PlatformTarget` is always `AnyCPU`.
- Existing `Release|x64` / `Release|x86` configurations in older csproj files are harmless
  leftovers; nothing builds them any more, and new projects do not need them.
