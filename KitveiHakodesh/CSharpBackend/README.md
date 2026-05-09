# KitveiHakodesh/CSharpBackend — C# Backend Projects

Contains the .NET libraries that back the KitveiHakodesh Vue app.

## Projects

| Folder                                                   | Purpose                                                             |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| [`KitveiHakodeshLib`](KitveiHakodeshLib/README.md)                     | WebView2 host, message bridge, DB/PDF/search handlers               |
| [`BloomSearchEngineLib`](BloomSearchEngineLib/README.md) | Bloom filter full-text search engine for seforim                    |
| `KitveiHakodeshDemoApp`                                         | Standalone WinForms demo app for testing KitveiHakodeshLib outside of Word |
| `WordToPdfConverter`                                     | Converts Word documents to PDF using the Word COM object            |

## Solution

`KitveiHakodesh.slnx` — builds all four projects. SDK-style projects (`KitveiHakodeshLib`, `BloomSearchEngineLib`) can also be built with `dotnet build`.
