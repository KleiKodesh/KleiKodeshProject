# Cache Preservation During Installer Updates

## Problem

During WPF installer updates, users were losing cached data from KitveiHakodesh:
- **Word→PDF conversions** (`KitveiHakodesh/cache/word/`)
- **HebrewBooks downloads** (`KitveiHakodesh/cache/hebrewbooks/`)
- **Bloom filter search index** (`BloomFilters/`)

The installer was extracting all files from the embedded zip, overwriting these user-generated caches.

## Solution

`AddinInstaller.ExtractAsync()` now checks if a file exists on disk before extraction. If it does, the file is skipped. This preserves user data and caches across updates while still allowing fresh installs to extract the default/empty folders.

### Files Preserved on Update

| Path | Reason |
|------|--------|
| `WebSitesWhitelist.json` | User's website list customization |
| `KitveiHakodesh/cache/word/*` | Cached Word→PDF conversions |
| `KitveiHakodesh/cache/hebrewbooks/*` | Cached HebrewBooks downloads |
| `KitveiHakodesh/webcache/*` | WebView2 browser cache |
| `BloomFilters/*` | Search index (rebuilt on version mismatch) |

### Implementation

**New method:** `AddinInstaller.ShouldSkipOnUpdate(string entryPath)`

Checks if an entry path matches any of the preserved patterns:
- Exact match: `WebSitesWhitelist.json`
- Prefix match: `KitveiHakodesh\cache\*`
- Prefix match: `BloomFilters\*`

**Updated method:** `AddinInstaller.ExtractAsync()`

Before extracting each entry:
```csharp
if (ShouldSkipOnUpdate(entry.FullName) && File.Exists(fullPath))
{
    // Skip this entry — preserve existing file
    continue;
}
```

## Behavior

### Fresh Install
- All files extracted, including empty cache folders
- `WebSitesWhitelist.json` extracted with default list

### Update (File Exists)
- App code and resources extracted (overwritten)
- Cache folders skipped (preserved)
- `WebSitesWhitelist.json` skipped (preserved)
- `BloomFilters/` skipped (preserved)

### Repair Mode
- Same as update — preserves caches

## Bloom Index Version Mismatch

The Bloom filter index is preserved across updates, but `SearchHandler.OnDbReady()` detects version mismatches:

1. Reads installed app version from registry: `HKCU\SOFTWARE\KleiKodesh\Version`
2. Reads index version from `BloomFilters/lines.ver`
3. If they differ, prompts user to rebuild the index
4. User confirms → full rebuild (overwrites old index)

This ensures the search index stays in sync with the app version without forcing a rebuild on every update.

## Related Files

- `Build/Installer/Helpers/AddinInstaller.cs` — extraction logic
- `Build/Installer/README.md` — extraction rules table
- `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/AppViewer.cs` — WebView2 user data folder
- `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/Pdf/PdfHandler.cs` — Word cache
- `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/HebrewBooks/HebrewBooksHandler.cs` — HebrewBooks cache
- `KitveiHakodesh/CSharpBackend/BloomSearchEngineLib/Search/SearchHandler.cs` — index version detection
