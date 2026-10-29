# KitveiHakodesh/CSharpBackend — C# Backend Projects

Contains the .NET libraries that back the KitveiHakodesh Vue app.

## Projects

| Folder | Purpose |
| ------ | ------- |
| [`KitveiHakodesh.Core`](KitveiHakodesh.Core/README.md) | All non-UI logic (seforim DB, FTS, catalog, dictionary, HebrewBooks, settings, annotations). net10.0-windows |
| `KitveiHakodesh.Core.Portable` | Core's OS-neutral subset compiled from Core's own sources on plain net10.0, for cross-platform tools |
| `KitveiHakodeshService` | Native AOT Windows service (net10.0-windows). Transport only: named-pipe Dispatcher over Core, plus loopback HTTP (`HttpHostServer`) for browser dev |
| `KitveiHakodeshService.Tests` | Tests for the service |
| [`KitveiHakodeshLib`](KitveiHakodeshLib/README.md) | net48 WebView2 host for the Vue app; relays RPC to the service (`Bridge/ServiceRpcRelay`). No reference to Core |
| [`KitveiHakodeshDemoApp`](KitveiHakodeshDemoApp/README.md) | Standalone WinForms demo app for testing KitveiHakodeshLib outside of Word |
| [`FtsLib-Csharp`](FtsLib-Csharp/README.md) | Generic full-text search engine, fed by Core |
| `DocConvertLib` | Document conversion (net48 + net10.0), referenced by Core |

## Request flow

- **Hosted (Word / DemoApp):** the Vue app posts to the virtual host `http://kitvei-service` ->
  KitveiHakodeshLib `ServiceRpcRelay` -> named pipe -> service Dispatcher -> Core.
- **Browser dev:** vite proxies `/khs-endpoint` -> service loopback HTTP (`HttpHostServer`) -> Core.

The service is transport only; Core owns all logic.

## Solution

`KitveiHakodesh.slnx` builds FluentChromeTabs, FtsLib, KitveiHakodesh.Core, KitveiHakodeshLib,
KitveiHakodeshDemoApp, KitveiHakodeshService and KitveiHakodeshService.Tests. FtsLib-Csharp has its
own solution file (`FtsLib-Csharp/FtsLib.slnx`) and can be built independently.
