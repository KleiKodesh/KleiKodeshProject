---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**/*.cs|**/vue-zayit/**/*.ts|**/vue-zayit/**/sqlQueries*'
---

# Zayit Database Architecture

## CRITICAL: Query Execution Pattern

Zayit uses a **generic SQL execution pattern** - SQL queries are defined in TypeScript and executed by C#:

```typescript
// TypeScript: sqlQueries.ts defines all SQL
export const SqlQueries = {
  getAllBooks: `SELECT Id, CategoryId, Title, ... FROM book`
}

// TypeScript: Service calls C# with SQL string
const result = await dbService.executeQuery(SqlQueries.getAllBooks)
```

```csharp
// C#: DbQueries.cs executes ANY SQL sent from TypeScript
public object ExecuteQuery(string sql, object[] parameters = null)
{
    return _db.DapperConnection.Query(sql).ToArray();
}
```

**Key Point**: C# doesn't define queries - it's a pass-through executor. All SQL lives in `sqlQueries.ts`.

## Database Schema

### Book Table
```sql
book
  - Id
  - CategoryId
  - Title
  - HeShortDesc
  - Path
  - OrderIndex
  - TotalLines
  - HasTargumConnection
  - HasReferenceConnection
  - HasCommentaryConnection
  - HasOtherConnection
  - HasSourceConnection
```

### Category Table
```sql
category
  - Id
  - ParentId
  - Title
  - Level
```

### Line Table
```sql
line
  - id
  - bookId
  - lineIndex
  - content
```

### Link Table
```sql
link
  - sourceLineId
  - targetLineId
  - targetBookId
  - connectionTypeId
```

### TOC Tables
```sql
tocEntry
  - id
  - bookId
  - parentId
  - textId
  - level
  - lineId
  - isLastChild
  - hasChildren

tocText
  - id
  - text

alt_toc_entry
  - id
  - structureId
  - parentId
  - textId
  - level
  - lineId
  - isLastChild
  - hasChildren

alt_toc_structure
  - id
  - bookId
```

## Adding New Database Fields

When adding a field to the database, update THREE places:

### 1. TypeScript Model (`src/types/Book.ts`)
```typescript
export interface Book {
    id: number
    // ... existing fields
    hasSourceConnection: number  // Add new field
}
```

### 2. SQL Query (`src/data/sqlQueries.ts`)
```typescript
getAllBooks: `
  SELECT 
    Id,
    CategoryId,
    Title,
    // ... existing fields
    HasSourceConnection  -- Add new field
  FROM book
  ORDER BY CategoryId
`
```

### 3. C# Model (`ZayitLib/Models/Book.cs`)
```csharp
public class Book
{
    public int Id { get; set; }
    // ... existing properties
    public int HasSourceConnection { get; set; }  // Add new property
}
```

**Note**: C# model is used for type safety but queries come from TypeScript.

## Database Location

```csharp
// DbManager.cs
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var databasePath = Path.Combine(
    appData,
    "io.github.kdroidfilter.seforimapp",
    "databases",
    "seforim.db"
);
```

**Path**: `%AppData%\io.github.kdroidfilter.seforimapp\databases\seforim.db`

## Query Patterns

### Simple Query (No Parameters)
```typescript
const categories = await dbService.executeQuery(SqlQueries.getAllCategories)
```

### Parameterized Query
```typescript
getToc: (docId: number) => `
  SELECT * FROM tocEntry WHERE bookId = ${docId}
`

const toc = await dbService.executeQuery(SqlQueries.getToc(bookId))
```

### Range Query
```typescript
getLineRange: (bookId: number, start: number, end: number) => `
  SELECT lineIndex, content 
  FROM line 
  WHERE bookId = ${bookId} 
    AND lineIndex >= ${start} 
    AND lineIndex <= ${end}
`
```

## Common Mistakes

❌ **WRONG**: Adding field only to C# model
```csharp
// C# model updated but SQL query not updated
public int HasSourceConnection { get; set; }
```
Result: Field is always null/undefined in TypeScript

❌ **WRONG**: Adding field only to TypeScript interface
```typescript
// TypeScript interface updated but SQL query not updated
hasSourceConnection: number
```
Result: TypeScript error - property doesn't exist on returned data

✅ **CORRECT**: Update all three places
1. TypeScript interface
2. SQL query in sqlQueries.ts
3. C# model (for type safety)

## Database Service Pattern

```typescript
// dbService.ts
class DbService {
  async executeQuery(sql: string): Promise<any[]> {
    const promise = this.bridge.createRequest('ExecuteQuery')
    this.bridge.send('ExecuteQuery', [sql])
    return await promise
  }
}
```

```csharp
// DbQueries.cs
public object ExecuteQuery(string sql, object[] parameters = null)
{
    if (_db?.DapperConnection == null) {
        return new object[0];
    }
    
    return _db.DapperConnection.Query(sql).ToArray();
}
```

**Key Points**:
- TypeScript sends SQL string to C#
- C# executes with Dapper (dynamic typing)
- Results map to TypeScript interfaces by property name
- Case-insensitive mapping (SQL: `HasSourceConnection` → TS: `hasSourceConnection`)
