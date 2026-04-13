# Kezayit/CSharpBackend — C# Backend Projects

Contains the .NET libraries that back the Kezayit Vue app.

## Projects

| Folder                                                   | Purpose                                                             |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| [`KezayitLib`](KezayitLib/README.md)                     | WebView2 host, message bridge, DB/PDF/search handlers               |
| [`BloomSearchEngineLib`](BloomSearchEngineLib/README.md) | Bloom filter full-text search engine for seforim                    |
| `KezayitDemoApp`                                         | Standalone WinForms demo app for testing KezayitLib outside of Word |
| `WordToPdfConverter`                                     | Converts Word documents to PDF using the Word COM object            |

## Solution

`Kezayit.slnx` — builds all four projects. SDK-style projects (`KezayitLib`, `BloomSearchEngineLib`) can also be built with `dotnet build`.
