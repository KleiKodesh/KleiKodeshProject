# KitveiHakodesh/CSharpBackend — C# Backend Projects

Contains the .NET libraries that back the KitveiHakodesh Vue app.

## Projects

| Folder                                                   | Purpose                                                             |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| [`KitveiHakodeshLib`](KitveiHakodeshLib/README.md)                     | WebView2 host, message bridge, DB/PDF/search handlers               |
| [`SearchEngine`](SearchEngine/README.md)                 | Lucene-based full-text search engine for seforim                    |
| `KitveiHakodeshDemoApp`                                         | Standalone WinForms demo app for testing KitveiHakodeshLib outside of Word |
| [`DocumentLocator`](DocumentLocator/README.md)           | NTFS MFT file index service + named-pipe client for local file search |

## Solution

`KitveiHakodesh.slnx` — builds KitveiHakodeshLib, SearchEngine, KitveiHakodeshDemoApp, DocumentLocator.Client, DocumentLocator.Service, and the core DocumentLocator library. SearchEngine has its own solution file (`SearchEngine/SearchEngine.sln`) and can be built independently.
