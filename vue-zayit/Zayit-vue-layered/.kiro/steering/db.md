# Data Layer Architecture

## Overview

Unified database manager that routes requests to C# WebView2 (production) or dev server (development). Both behave identically with promise-based API.

**Key Principle**: SQL queries are defined once in `sqlQueries.ts` and used by both development (Vite) and production (C#) environments.

## Usage

```typescript
import { dbManager } from '@/data/dbManager'

// All operations return promises
const { categoriesFlat, booksFlat } = await dbManager.getTree()
const { tocEntriesFlat } = await dbManager.getToc(bookId)
const links = await dbManager.getLinks(lineId, tabId, bookId)
const totalLines = await dbManager.getTotalLines(bookId)
const lines = await dbManager.loadLineRange(bookId, start, end)
```

## Architecture

```
Component
    ↓
dbManager (routing)
    ↓
├─ csharpBridge → C# WebView2 → seforim.db (production)
└─ sqliteDb → Vite API → seforim.db (development)
```

## File Responsibilities

| File | Purpose |
|------|---------|
| `sqlQueries.ts` | **Single source of truth** - All SQL query definitions |
| `dbManager.ts` | Routes requests based on environment |
| `csharpBridge.ts` | Handles C# WebView2 communication (production) |
| `sqliteDb.ts` | Executes queries via Vite dev server (development) |

## Rules

1. **All SQL in `sqlQueries.ts`** - No SQL strings elsewhere
2. **Components use `dbManager` only** - Never import sqliteDb or csharpBridge directly
3. **No code duplication** - SQL defined once, used everywhere
4. **C# uses TypeScript SQL** - C# copies SQL from sqlQueries.ts, no separate C# SQL definitions

## SQL Query Flow

### Development Mode
1. Component calls `dbManager.getTree()`
2. dbManager routes to `sqliteDb.getAllCategories()`
3. sqliteDb uses SQL from `SqlQueries.getAllCategories`
4. Vite plugin executes query against database
5. Results returned to component

### Production Mode
1. Component calls `dbManager.getTree()`
2. dbManager routes to `csharpBridge.send('GetTree')`
3. C# receives command via WebView2
4. C# executes SQL (copied from sqlQueries.ts)
5. C# calls `window.receiveTreeData(json)`
6. csharpBridge resolves promise with results
7. Results returned to component

## C# Integration

### Commands

| Command | Args | Response |
|---------|------|----------|
| `GetTree` | `[]` | `window.receiveTreeData(data)` |
| `GetToc` | `[bookId]` | `window.receiveTocData(bookId, data)` |
| `GetLinks` | `[lineId, tabId, bookId]` | `window.receiveLinks(tabId, bookId, links)` |
| `GetTotalLines` | `[bookId]` | `window.receiveTotalLines(bookId, totalLines)` |
| `GetLineContent` | `[bookId, lineIndex]` | `window.receiveLineContent(bookId, lineIndex, content)` |
| `GetLineId` | `[bookId, lineIndex]` | `window.receiveLineId(bookId, lineIndex, lineId)` |
| `GetLineRange` | `[bookId, start, end]` | `window.receiveLineRange(bookId, start, end, lines)` |

### C# Implementation

C# methods copy SQL queries from `sqlQueries.ts`:

```csharp
private async void GetTree()
{
    // SQL copied from sqlQueries.ts: SqlQueries.getAllCategories
    var categories = DbQueries.ExecuteQuery(@"
        SELECT DISTINCT 
          Id,
          ParentId,
          Title,
          Level
        FROM category
        ORDER BY Level, Id
    ");

    // SQL copied from sqlQueries.ts: SqlQueries.getAllBooks
    var books = DbQueries.ExecuteQuery(@"
        SELECT 
          Id,
          CategoryId,
          Title,
          HeShortDesc,
          OrderIndex,
          TotalLines,
          HasTargumConnection,
          HasReferenceConnection,
          HasCommentaryConnection,
          HasOtherConnection
        FROM book
        ORDER BY CategoryId
    ");

    var treeData = new { categoriesFlat = categories, booksFlat = books };
    string json = JsonSerializer.Serialize(treeData, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    await ExecuteScriptAsync($"window.receiveTreeData({json});");
}
```

## Adding New Operations

1. Add SQL to `sqlQueries.ts`
2. Add function to `sqliteDb.ts` using the SQL
3. Add response handler to `csharpBridge.ts`
4. Add routing method to `dbManager.ts`
5. Implement C# handler (copy SQL from sqlQueries.ts)

Done.

## Benefits

- **Single source of truth**: SQL defined once in TypeScript
- **Consistency**: Same queries in dev and production
- **Maintainability**: Update SQL in one place
- **Type safety**: TypeScript provides query parameter validation
- **No duplication**: C# doesn't maintain separate SQL definitions
