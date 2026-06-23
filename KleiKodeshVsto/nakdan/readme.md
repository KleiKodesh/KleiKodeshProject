# Nakdan

.NET library for Hebrew diacritical mark (Nikkud) management via the Dicta API.

## Solution Structure

```
Nakdan.slnx              # Solution file (SLNX format)
├── Nakdan/              # Class library — core engine, UI controls, Word style management
│   ├── Core/            # Dicta API client, OOXML parsing, tokenization
│   ├── Helpers/         # Settings manager, VSTO integration helpers
│   ├── UI/              # WPF user controls (NakdanView, NakdanDictionary)
│   └── WdStyles/        # Word document style reading and filtering
├── NakdanDemo/          # WPF demo application (no Word required)
├── sources/             # Reference: unpacked CulmusOOoNakdan extension
├── packages/            # NuGet package cache
└── Nakdan.svg           # Project icon
```

## Projects

### Nakdan (class library)

The main library providing vowelization functionality:

| Subfolder | Key Files | Purpose |
|-----------|-----------|---------|
| `Core/` | `NakdanEngine.cs`, `NakdanWrapper.cs`, `DictaApiClient.cs`, `Token.cs`, `TokenChunker.cs`, `OoxmlHelper.cs`, `RunInfo.cs`, `RunWriter.cs`, `TokenTextConverter.cs`, `HebrewTextExtensions.cs`, `NakdanCofiguration.cs` | API communication, OOXML parsing, token stream processing, text chunking, parallel API calls |
| `Helpers/` | `SettingsManager.cs`, `VstoHelper.cs` | Persistent settings, Word interop helpers |
| `UI/` | `NakdanView.xaml/.cs`, `NakdanViewModel.cs`, `NakdanDictionary.xaml`, `Converters.cs` | WPF controls for vowelization UI and dictionary display |
| `WdStyles/` | `DocumentStyle.cs`, `DocumentStyleProvider.cs`, `StyleExtractor.cs`, `StyleItem.cs`, `StyleNameResolver.cs` | Word style enumeration, ignored-style filtering |

### NakdanDemo

WPF demo application for testing the Nakdan library outside of Word.

### sources/

Contains the unpacked [CulmusOOoNakdan](https://sourceforge.net/projects/culmus/files/language_tools/) extension files (`.oxt` → `.zip` → `.jar`) for reference when implementing vowelization logic.

## Key Dependencies

- **Dicta API** — cloud-based Hebrew vowelization service
- **System.Text.Json** — JSON serialization for API communication
- **System.Net.Http** — HTTP client for API calls
