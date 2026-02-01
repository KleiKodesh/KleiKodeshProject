# Data Layer Architecture

## Overview

Unified database manager that routes requests to C# WebView2 (production) or dev server (development). Both behave identically with promise-based API.

**Key Principle**: SQL queries are defined once in `sqlQueries.ts` and used by both development (Vite) and production (C#) environments.

## Usage

```typescript
import { dbManager } from "@/data/dbManager";

// All operations return promises
const { categoriesFlat, booksFlat } = await dbManager.getTree();
const { tocEntriesFlat } = await dbManager.getToc(bookId);
const links = await dbManager.getLinks(lineId, tabId, bookId);
const totalLines = await dbManager.getTotalLines(bookId);
const lines = await dbManager.loadLineRange(bookId, start, end);
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

| File              | Purpose                                                |
| ----------------- | ------------------------------------------------------ |
| `sqlQueries.ts`   | **Single source of truth** - All SQL query definitions |
| `dbManager.ts`    | Routes requests based on environment                   |
| `csharpBridge.ts` | Handles C# WebView2 communication (production)         |
| `sqliteDb.ts`     | Executes queries via Vite dev server (development)     |

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

| Command          | Args                      | Response                                                |
| ---------------- | ------------------------- | ------------------------------------------------------- |
| `GetTree`        | `[]`                      | `window.receiveTreeData(data)`                          |
| `GetToc`         | `[bookId]`                | `window.receiveTocData(bookId, data)`                   |
| `GetLinks`       | `[lineId, tabId, bookId]` | `window.receiveLinks(tabId, bookId, links)`             |
| `GetTotalLines`  | `[bookId]`                | `window.receiveTotalLines(bookId, totalLines)`          |
| `GetLineContent` | `[bookId, lineIndex]`     | `window.receiveLineContent(bookId, lineIndex, content)` |
| `GetLineId`      | `[bookId, lineIndex]`     | `window.receiveLineId(bookId, lineIndex, lineId)`       |
| `GetLineRange`   | `[bookId, start, end]`    | `window.receiveLineRange(bookId, start, end, lines)`    |

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

## Database Schema

### Core Tables

#### Categories

- **category**: Hierarchical book categories
  - `id` (INTEGER, PK): Unique category identifier
  - `parentId` (INTEGER): Parent category reference
  - `title` (TEXT, NOT NULL): Category display name
  - `level` (INTEGER, NOT NULL, default 0): Hierarchy depth
  - `orderIndex` (INTEGER, NOT NULL, default 999): Display order

- **category_closure**: Transitive closure for category hierarchy
  - `ancestorId` (INTEGER, PK): Ancestor category ID
  - `descendantId` (INTEGER, PK): Descendant category ID

#### Books

- **book**: Main book records
  - `id` (INTEGER, PK): Unique book identifier
  - `categoryId` (INTEGER, NOT NULL): Category reference
  - `sourceId` (INTEGER, NOT NULL): Source reference
  - `title` (TEXT, NOT NULL): Book title
  - `heShortDesc` (TEXT): Hebrew short description
  - `notesContent` (TEXT): Additional notes
  - `orderIndex` (INTEGER, NOT NULL, default 999): Display order
  - `totalLines` (INTEGER, NOT NULL, default 0): Total line count
  - `isBaseBook` (INTEGER, NOT NULL, default 0): Base text flag
  - Connection flags: `hasTargumConnection`, `hasReferenceConnection`, `hasSourceConnection`, `hasCommentaryConnection`, `hasOtherConnection`
  - Structure flags: `hasAltStructures`, `hasTeamim`, `hasNekudot`

#### Content

- **line**: Book content lines
  - `id` (INTEGER, PK): Unique line identifier
  - `bookId` (INTEGER, NOT NULL): Book reference
  - `lineIndex` (INTEGER, NOT NULL): Line position in book
  - `content` (TEXT, NOT NULL): Line text content
  - `heRef` (TEXT): Hebrew reference
  - `tocEntryId` (INTEGER): Table of contents reference
  - `blockId` (INTEGER): Content block reference

#### Table of Contents

- **tocText**: TOC text entries
  - `id` (INTEGER, PK): Unique text identifier
  - `text` (TEXT, NOT NULL): Display text

- **tocEntry**: TOC structure
  - `id` (INTEGER, PK): Unique entry identifier
  - `bookId` (INTEGER, NOT NULL): Book reference
  - `parentId` (INTEGER): Parent entry reference
  - `textId` (INTEGER, NOT NULL): Text reference
  - `level` (INTEGER, NOT NULL): Hierarchy depth
  - `lineId` (INTEGER): Associated line reference
  - `isLastChild` (INTEGER, NOT NULL, default 0): Last child flag
  - `hasChildren` (INTEGER, NOT NULL, default 0): Has children flag

- **line_toc**: Line-TOC associations
  - `lineId` (INTEGER, PK): Line reference
  - `tocEntryId` (INTEGER, NOT NULL): TOC entry reference

#### Alternative TOC Structures

- **alt_toc_structure**: Alternative TOC definitions
  - `id` (INTEGER, PK): Structure identifier
  - `bookId` (INTEGER, NOT NULL): Book reference
  - `key` (TEXT, NOT NULL): Structure key
  - `title` (TEXT): English title
  - `heTitle` (TEXT): Hebrew title

- **alt_toc_entry**: Alternative TOC entries
  - `id` (INTEGER, PK): Entry identifier
  - `structureId` (INTEGER, NOT NULL): Structure reference
  - `parentId` (INTEGER): Parent entry reference
  - `textId` (INTEGER, NOT NULL): Text reference
  - `level` (INTEGER, NOT NULL): Hierarchy depth
  - `lineId` (INTEGER): Associated line reference
  - `isLastChild` (INTEGER, NOT NULL, default 0): Last child flag
  - `hasChildren` (INTEGER, NOT NULL, default 0): Has children flag

- **line_alt_toc**: Line-alternative TOC associations
  - `lineId` (INTEGER, PK): Line reference
  - `structureId` (INTEGER, PK): Structure reference
  - `altTocEntryId` (INTEGER, NOT NULL): Alternative TOC entry reference

#### Links and Connections

- **connection_type**: Link type definitions
  - `id` (INTEGER, PK): Type identifier
  - `name` (TEXT, NOT NULL): Type name

- **link**: Inter-book connections
  - `id` (INTEGER, PK): Link identifier
  - `sourceBookId` (INTEGER, NOT NULL): Source book reference
  - `targetBookId` (INTEGER, NOT NULL): Target book reference
  - `sourceLineId` (INTEGER, NOT NULL): Source line reference
  - `targetLineId` (INTEGER, NOT NULL): Target line reference
  - `connectionTypeId` (INTEGER, NOT NULL): Connection type reference

- **book_has_links**: Book link summary
  - `bookId` (INTEGER, PK): Book reference
  - `hasSourceLinks` (INTEGER, NOT NULL, default 0): Has outgoing links flag
  - `hasTargetLinks` (INTEGER, NOT NULL, default 0): Has incoming links flag

#### Metadata Tables

- **author**: Author records
  - `id` (INTEGER, PK): Author identifier
  - `name` (TEXT, NOT NULL): Author name

- **topic**: Topic classifications
  - `id` (INTEGER, PK): Topic identifier
  - `name` (TEXT, NOT NULL): Topic name

- **pub_place**: Publication places
  - `id` (INTEGER, PK): Place identifier
  - `name` (TEXT, NOT NULL): Place name

- **pub_date**: Publication dates
  - `id` (INTEGER, PK): Date identifier
  - `date` (TEXT, NOT NULL): Date string

- **source**: Source records
  - `id` (INTEGER, PK): Source identifier
  - `name` (TEXT, NOT NULL): Source name

#### Association Tables

- **book_author**: Book-author associations
  - `bookId` (INTEGER, PK): Book reference
  - `authorId` (INTEGER, PK): Author reference

- **book_topic**: Book-topic associations
  - `bookId` (INTEGER, PK): Book reference
  - `topicId` (INTEGER, PK): Topic reference

- **book_pub_place**: Book-publication place associations
  - `bookId` (INTEGER, PK): Book reference
  - `pubPlaceId` (INTEGER, PK): Publication place reference

- **book_pub_date**: Book-publication date associations
  - `bookId` (INTEGER, PK): Book reference
  - `pubDateId` (INTEGER, PK): Publication date reference

#### Book Configuration

- **book_acronym**: Book acronyms and abbreviations
  - `bookId` (INTEGER, PK): Book reference
  - `term` (TEXT, PK): Acronym or abbreviation

- **default_commentator**: Default commentaries for books
  - `bookId` (INTEGER, PK): Base book reference
  - `commentatorBookId` (INTEGER, PK): Commentary book reference
  - `position` (INTEGER, NOT NULL): Display position

- **default_targum**: Default translations for books
  - `bookId` (INTEGER, PK): Base book reference
  - `targumBookId` (INTEGER, PK): Translation book reference
  - `position` (INTEGER, NOT NULL): Display position

### Key Relationships

1. **Category Hierarchy**: `category.parentId` → `category.id` with closure table support
2. **Book Content**: `book.id` → `line.bookId` (one-to-many)
3. **TOC Structure**: `tocEntry.parentId` → `tocEntry.id` with line associations
4. **Cross-References**: `link` table connects lines across books
5. **Metadata Associations**: Many-to-many relationships via junction tables

## Benefits

- **Single source of truth**: SQL defined once in TypeScript
- **Consistency**: Same queries in dev and production
- **Maintainability**: Update SQL in one place
- **Type safety**: TypeScript provides query parameter validation
- **No duplication**: C# doesn't maintain separate SQL definitions
