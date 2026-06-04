---
inclusion: fileMatch
fileMatchPattern: '**/CSharpBackend/**|**/build/**'
---

# C# Build Environment

## Tool Locations

- MSBuild: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- nuget.exe: not installed globally. Download on demand from `https://dist.nuget.org/win-x86-commandline/latest/nuget.exe` and delete after use.

## NuGet Package Restore

The `packages/` folders are gitignored and must be restored on every fresh clone before building. The projects use the classic `packages.config` format — `dotnet restore` and MSBuild's `/t:Restore` target do not populate the local `packages/` folder for this format. Only `nuget.exe restore` works.

Restore commands (run from the workspace root):

```
.\nuget.exe restore "CSharpBackend\SearchEngine\SearchEngine.sln" -PackagesDirectory "CSharpBackend\SearchEngine\packages"
.\nuget.exe restore "CSharpBackend\KitveiHakodesh.slnx"
```

The first restore (`SearchEngine.sln`) covers the Lucene-based search engine; the second restore (`KitveiHakodesh.slnx`) covers `KitveiHakodeshLib`, `KitveiHakodeshDemoApp`, `SearchEngine`, and `EverythingSearchClient` — their packages folder is `CSharpBackend\packages\` (the default, no `-PackagesDirectory` override needed).

## Building

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "CSharpBackend\KitveiHakodesh.slnx" /p:Configuration=Release /v:minimal
```

The build also runs `npm run build` and copies the Vue bundle to `bin\Release\KitveiHakodesh\` via `KitveiHakodesh.targets`.
