# KitveiHakodesh/CSharpBackend — C# Backend Projects

Contains the .NET libraries that back the KitveiHakodesh Vue app.

## Projects

| Folder                                                   | Purpose                                                             |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| [`KitveiHakodeshLib`](KitveiHakodeshLib/README.md)                     | WebView2 host, message bridge, DB/PDF/search handlers               |
| [`Ftslib-Csharp`](Ftslib-Csharp/README.md)              | Custom LSM-style full-text search index for seforim                 |
| [`KitveiHakodeshDemoApp`](KitveiHakodeshDemoApp/README.md)                         | Standalone WinForms demo app for testing KitveiHakodeshLib outside of Word |
| [`DocumentLocator`](DocumentLocator/README.md)           | NTFS MFT file index service + named-pipe client for local file search |
| [`KitveiHakodeshService`](KitveiHakodeshService/README.md) | .NET 10 Worker Service that re-hosts the app features over HTTP, outside of VSTO/WebView2 |

## Solution

`KitveiHakodesh.slnx` — builds KitveiHakodeshLib, Ftslib-Csharp, KitveiHakodeshDemoApp, DocumentLocator.Client, DocumentLocator.Service, and the core DocumentLocator library. Ftslib-Csharp has its own solution file (`Ftslib-Csharp/FtsLib.slnx`) and can be built independently.
