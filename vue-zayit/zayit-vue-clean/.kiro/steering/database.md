---
inclusion: manual
---

# Database

## Architecture

All SQLite access is unified through two files:

- `src/db/queries.sql.ts` — every raw SQL string, nothing else. No inline SQL anywhere else in the codebase.
- `src/db/db.ts` — the `query<T>(sql, params?)` client. Reads `VITE_DB_URL` from env.

Feature composables call `query()` with strings from `queries.sql.ts`. Components never touch the DB layer directly.

```ts
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

const books = await query<Book>(SQL.GET_ALL_BOOKS)
```

## Transport

Two transports only — selected automatically at runtime, no config needed in app code:

1. C# WebView host — when `window.__webviewQuery` is present (injected by the host before the app boots)
2. HTTP dev server — fallback when running in a browser during development

### C# WebView contract

The host must inject `window.__webviewQuery` before the app loads:

```js
window.__webviewQuery = async (sql, params) => {
  // call C# bridge, return:
  return { rows: [ /* result objects */ ] }
}
```

### Dev server

- Run: `npm run dev:server` (node server/index.js)
- `DB_PATH` env var sets the `.db` file (default: `./data.db`)
- `PORT` env var sets the port (default: `4000`)
- Override URL via `VITE_DB_URL` in `.env.development` if needed

## Schema

Only use tables explicitly requested. Schema reference below.

### category
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| parentId | INTEGER | nullable, self-ref |
| title | TEXT | not null |
| level | INTEGER | not null, default 0 |
| orderIndex | INTEGER | not null, default 999 |

### category_closure
Closure table for category hierarchy.
| column | type | notes |
|---|---|---|
| ancestorId | INTEGER | PK (composite) |
| descendantId | INTEGER | PK (composite) |

### author
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| name | TEXT | not null |

### topic
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| name | TEXT | not null |

### pub_place
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| name | TEXT | not null |

### pub_date
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| date | TEXT | not null |

### source
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| name | TEXT | not null |

### book
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| categoryId | INTEGER | FK → category |
| sourceId | INTEGER | FK → source |
| title | TEXT | not null |
| heShortDesc | TEXT | nullable |
| notesContent | TEXT | nullable |
| orderIndex | INTEGER | default 999 |
| totalLines | INTEGER | default 0 |
| isBaseBook | INTEGER | 0/1 bool |
| hasTargumConnection | INTEGER | 0/1 bool |
| hasReferenceConnection | INTEGER | 0/1 bool |
| hasSourceConnection | INTEGER | 0/1 bool |
| hasCommentaryConnection | INTEGER | 0/1 bool |
| hasOtherConnection | INTEGER | 0/1 bool |
| hasAltStructures | INTEGER | 0/1 bool |
| hasTeamim | INTEGER | 0/1 bool |
| hasNekudot | INTEGER | 0/1 bool |
| externalLibraryId | INTEGER | nullable |

### book_pub_place
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| pubPlaceId | INTEGER | PK (composite), FK → pub_place |

### book_pub_date
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| pubDateId | INTEGER | PK (composite), FK → pub_date |

### book_topic
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| topicId | INTEGER | PK (composite), FK → topic |

### book_author
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| authorId | INTEGER | PK (composite), FK → author |

### line
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| lineIndex | INTEGER | not null |
| content | TEXT | not null |
| tocEntryId | INTEGER | nullable |
| chunk_id | INTEGER | nullable |

### tocText
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| text | TEXT | not null |

### tocEntry
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| parentId | INTEGER | nullable, self-ref |
| textId | INTEGER | FK → tocText |
| level | INTEGER | not null |
| lineId | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool |
| hasChildren | INTEGER | 0/1 bool |

### connection_type
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| name | TEXT | not null |

### link
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| sourceBookId | INTEGER | FK → book |
| targetBookId | INTEGER | FK → book |
| sourceLineId | INTEGER | FK → line |
| targetLineId | INTEGER | FK → line |
| connectionTypeId | INTEGER | FK → connection_type |

### book_has_links
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK |
| hasSourceLinks | INTEGER | 0/1 bool |
| hasTargetLinks | INTEGER | 0/1 bool |

### line_toc
| column | type | notes |
|---|---|---|
| lineId | INTEGER | PK |
| tocEntryId | INTEGER | FK → tocEntry |

### alt_toc_structure
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| key | TEXT | not null |
| title | TEXT | nullable |
| heTitle | TEXT | nullable |

### alt_toc_entry
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| structureId | INTEGER | FK → alt_toc_structure |
| parentId | INTEGER | nullable, self-ref |
| textId | INTEGER | FK → tocText |
| level | INTEGER | not null |
| lineId | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool |
| hasChildren | INTEGER | 0/1 bool |

### line_alt_toc
| column | type | notes |
|---|---|---|
| lineId | INTEGER | PK (composite), FK → line |
| structureId | INTEGER | PK (composite), FK → alt_toc_structure |
| altTocEntryId | INTEGER | FK → alt_toc_entry |

### book_acronym
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| term | TEXT | PK (composite) |

### default_commentator
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| commentatorBookId | INTEGER | PK (composite), FK → book |
| position | INTEGER | not null |

### default_targum
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK (composite), FK → book |
| targumBookId | INTEGER | PK (composite), FK → book |
| position | INTEGER | not null |

### bloom_metadata
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| chunk_size | INTEGER | not null |
