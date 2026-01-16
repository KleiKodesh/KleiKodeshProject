# SQL Queries Deduplication Implementation

## Problem
SQL queries were duplicated between:
- **C#**: `vue-zayit/Zayit-cs/ZayitLib/SeforimDb/SqlQueries.cs`
- **TypeScript**: `vue-zayit/zayit-vue/src/data/sqlQueries.ts`

This created maintenance overhead - any query change required updating both files.

## Solution
**Vue as Single Source of Truth**: Vue sends SQL query strings as parameters to C# commands.

### Architecture

```
Vue sqlQueries.ts (Single Source)
    ↓
dbManager sends SQL as parameter
    ↓ bridge.send('GetToc', [bookId, sqlQuery])
C# receives SQL string as parameter
    ↓
C# executes the SQL directly
```

## Implementation Details

### 1. C# Side - Accept SQL as Parameters

**Files Modified**:
- `ZayitViewerCommands.cs` - Updated method signatures to accept SQL strings
- `ZayitViewerDbCommands.cs` - All methods now accept `sqlQuery` parameter
- `ZayitLib.csproj` - Removed SqlQueries.cs reference
- **Deleted**: `SqlQueries.cs` - No longer needed

**Example**:
```csharp
// BEFORE:
public async void GetToc(int bookId)
{
    var tocEntries = _db.ExecuteQuery(SeforimDb.SqlQueries.GetToc(bookId));
}

// AFTER:
public async void GetToc(int bookId, string sqlQuery)
{
    var tocEntries = _db.ExecuteQuery(sqlQuery);
}
```

### 2. Vue Side - Send SQL with Commands

**File Modified**: `dbManager.ts`

**Example**:
```typescript
// BEFORE:
this.csharp.send('GetToc', [bookId])

// AFTER:
this.csharp.send('GetToc', [bookId, SqlQueries.getToc(bookId)])
```

## Benefits

1. **Single Source of Truth**: SQL queries defined only in TypeScript
2. **No Duplication**: C# receives queries from Vue, no separate class needed
3. **Easy Maintenance**: Update queries in one place only
4. **Simpler C# Code**: No SqlQueries class to maintain
5. **Flexible**: Vue controls the SQL, C# just executes it

## Files Changed

### C# Files
- `ZayitLib/Viewer/ZayitViewerCommands.cs` - Added SQL parameters to delegation methods
- `ZayitLib/Viewer/ZayitViewerDbCommands.cs` - All methods accept `sqlQuery` parameter
- `ZayitLib/ZayitLib.csproj` - Removed SqlQueries.cs reference
- **Deleted**: `ZayitLib/SeforimDb/SqlQueries.cs`

### TypeScript Files
- `zayit-vue/src/data/dbManager.ts` - Send SQL queries with all commands

## Testing Checklist

- [x] Build C# project successfully
- [x] Build Vue project successfully
- [ ] Test in WebView2 (production mode) - verify all database queries work
- [ ] Test in Vite dev server (development mode) - verify fallback works

## Migration Notes

**Breaking Change**: C# method signatures changed to include SQL query parameter. The bridge automatically passes the correct number of parameters via reflection, so no manual updates needed to calling code.
