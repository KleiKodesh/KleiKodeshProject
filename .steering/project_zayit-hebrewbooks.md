---
inclusion: fileMatch
fileMatchPattern: '**/HebrewBooks*|**/hebrewBooks*'
---

# Zayit Hebrew Books System

## CRITICAL: Hebrew Books Download & Cache Architecture

### System Overview
The Hebrew books system handles downloading PDFs from hebrewbooks.org and caching them for offline viewing.

**Components:**
- `HebrewBooksDownloadManager` - Core download and cache logic
- `HebrewBooksCommands` - Command interface wrapper
- `hebrewBooksStore.ts` - Vue store for Hebrew books functionality

### File Naming Convention
**Pattern**: `{sanitized-title}_{bookId}.pdf`
**Examples**: 
- `Sefer_HaZohar_Volume_1_12345.pdf`
- `Talmud_Bavli_Berachot_67890.pdf`

### Cache Directory Structure
```
Html/pdfjs/web/hebrewbookscache/
├── Sefer_HaZohar_Volume_1_12345.pdf
├── Talmud_Bavli_Berachot_67890.pdf
└── Mishnah_Zeraim_11111.pdf
```

### Command Flow - Hebrew Book Opening
1. User clicks Hebrew book in library
2. Vue sends `PrepareHebrewBookDownload` command to C#
3. C# downloads from hebrewbooks.org to `Html/pdfjs/web/hebrewbookscache/{sanitized-title}_{bookId}.pdf`
4. PDF.js opens via virtual host URL: `https://zayitHost/pdfjs/web/hebrewbookscache/{sanitized-title}_{bookId}.pdf`

### Bridge Communication Pattern

**C# → Vue Response Pattern:**
```csharp
// For cached files (immediate response)
await SendDownloadComplete(bookId, fileName);

// For non-cached files (after download)
await SendReady(bookId, action);
// ... download happens ...
await SendDownloadComplete(bookId, fileName);
```

**Vue → C# Request Pattern:**
```typescript
// Send command
csharp.send('PrepareHebrewBookDownload', [bookId, title, 'view'])

// Handle responses
const readyPromise = csharp.createRequest(`PrepareHebrewBookDownload:${bookId}:view`)
const downloadCompletePromise = csharp.createRequest(`HebrewBookDownloadComplete:${bookId}`)
```

### URL Construction
**Pattern**: `https://zayitHost/pdfjs/web/hebrewbookscache/{fileName}.pdf`
**Example**: `https://zayitHost/pdfjs/web/hebrewbookscache/Sefer_HaZohar_Volume_1_12345.pdf`

### Cache Management
- **Max Files**: 10 PDFs
- **Strategy**: Delete oldest by last access time
- **Scope**: Only manages files in hebrewbookscache directory
- **Pattern**: Files with `{title}_{id}.pdf` naming

### Download Actions
**View Action**: Cache in hebrewbookscache directory for immediate viewing
**Download Action**: Save to user-selected location via SaveFileDialog

### Error Handling Pattern

**C# Side:**
```csharp
try {
    // Hebrew book operation
    await SendDownloadComplete(bookId, fileName);
} catch (Exception ex) {
    Console.WriteLine($"[HebrewBooks] Error: {ex}");
    await SendDownloadComplete(bookId, null, false);
}
```

**Vue Side:**
```typescript
try {
    const result = await hebrewBooksStore.openHebrewBookViewer(bookId, title)
    // Handle success
} catch (error) {
    console.error('[HebrewBooks] Failed:', error)
    // Handle error
}
```

### File Sanitization Rules
- Remove invalid filename characters
- Replace spaces with underscores
- Limit length to 100 characters
- Fallback to "unknown" if empty

### Integration Points
- **Virtual Host**: Maps `zayitHost` to Html directory
- **PDF.js**: Uses standard PDF.js viewer with cached files
- **Tab System**: Integrates with Vue tab store for PDF state management

### Session Persistence
- Handled by existing `HebrewBooksDownloadManager`
- Uses different session mechanism than local PDFs
- Managed by Vue tab store for PDF state

### Common Issues & Solutions

**Issue**: Hebrew book not loading
**Check**: 
1. Cache directory exists and is writable
2. Virtual host mapping is correct
3. Bridge handlers are registered before C# sends response

**Issue**: Filename collisions
**Solution**: The `title_id` pattern ensures uniqueness even with duplicate titles

**Issue**: Cache not clearing
**Check**: ManageCache() is called after successful downloads

### Development Notes
- Hebrew books are identified by hebrewbooks.org domain in download URL
- Cache management only affects Hebrew book files (not other PDFs)
- File naming uses underscores for maximum filesystem compatibility
- Bridge communication follows promise-based request/response pattern
- **NEVER mix with Local PDF system** - they have different purposes and file patterns