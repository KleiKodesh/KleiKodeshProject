# Database Schema Reference

## Overview

The seforim database is SQLite. All SQLite access goes through `src/webview-host/db.ts` — never call fetch against the DB from a component or composable. All raw SQL strings live in `src/webview-host/queries.sql.ts` — no inline SQL anywhere else.

Feature composables call `query()` with a SQL constant from `queries.sql.ts` and a params array.

**Exception — dictionary DB**: dictionary SQL lives in `src/webview-host/dictionaryDb.ts`, not in `queries.sql.ts`. Both the C# host path (`__webviewDictQuery`) and the dev path (`devQueryDict`) execute the same SQL string sent from the frontend — there is nothing to keep in sync between C# and dev for dictionary queries.

---

## Tables

### category

| column     | type    | notes                 |
| ---------- | ------- | --------------------- |
| id         | INTEGER | PK                    |
| parentId   | INTEGER | nullable, self-ref    |
| title      | TEXT    | not null              |
| level      | INTEGER | not null, default 0   |
| orderIndex | INTEGER | not null, default 999 |

### category_closure

Closure table for category hierarchy.

| column       | type    | notes          |
| ------------ | ------- | -------------- |
| ancestorId   | INTEGER | PK (composite) |
| descendantId | INTEGER | PK (composite) |

### author / topic / pub_place / pub_date / source

Simple lookup tables: `id` PK, `name` TEXT not null.

### book

| column                  | type    | notes         |
| ----------------------- | ------- | ------------- |
| id                      | INTEGER | PK            |
| categoryId              | INTEGER | FK → category |
| sourceId                | INTEGER | FK → source   |
| title                   | TEXT    | not null      |
| heShortDesc             | TEXT    | nullable      |
| notesContent            | TEXT    | nullable      |
| orderIndex              | INTEGER | default 999   |
| totalLines              | INTEGER | default 0     |
| isBaseBook              | INTEGER | 0/1 bool      |
| hasTargumConnection     | INTEGER | 0/1 bool      |
| hasReferenceConnection  | INTEGER | 0/1 bool      |
| hasSourceConnection     | INTEGER | 0/1 bool      |
| hasCommentaryConnection | INTEGER | 0/1 bool      |
| hasOtherConnection      | INTEGER | 0/1 bool      |
| hasAltStructures        | INTEGER | 0/1 bool      |
| hasTeamim               | INTEGER | 0/1 bool      |
| hasNekudot              | INTEGER | 0/1 bool      |
| externalLibraryId       | INTEGER | nullable      |

### book_pub_place / book_pub_date / book_topic / book_author

Junction tables: `bookId` + respective FK, composite PK.

### line

| column    | type    | notes     |
| --------- | ------- | --------- |
| id        | INTEGER | PK        |
| bookId    | INTEGER | FK → book |
| lineIndex | INTEGER | not null  |
| content   | TEXT    | not null  |

### tocText

`id` PK, `text` TEXT not null.

### tocEntry

| column      | type    | notes               |
| ----------- | ------- | ------------------- |
| id          | INTEGER | PK                  |
| bookId      | INTEGER | FK → book           |
| parentId    | INTEGER | nullable, self-ref  |
| textId      | INTEGER | FK → tocText        |
| level       | INTEGER | not null            |
| lineId      | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool            |
| hasChildren | INTEGER | 0/1 bool            |

### connection_type

`id` PK, `name` TEXT not null.

### link

| column           | type    | notes                |
| ---------------- | ------- | -------------------- |
| id               | INTEGER | PK                   |
| sourceBookId     | INTEGER | FK → book            |
| targetBookId     | INTEGER | FK → book            |
| sourceLineId     | INTEGER | FK → line            |
| targetLineId     | INTEGER | FK → line            |
| connectionTypeId | INTEGER | FK → connection_type |

### book_has_links

| column         | type    | notes    |
| -------------- | ------- | -------- |
| bookId         | INTEGER | PK       |
| hasSourceLinks | INTEGER | 0/1 bool |
| hasTargetLinks | INTEGER | 0/1 bool |

### line_toc

`lineId` PK, `tocEntryId` FK → tocEntry.

### alt_toc_structure

| column  | type    | notes     |
| ------- | ------- | --------- |
| id      | INTEGER | PK        |
| bookId  | INTEGER | FK → book |
| key     | TEXT    | not null  |
| title   | TEXT    | nullable  |
| heTitle | TEXT    | nullable  |

### alt_toc_entry

| column      | type    | notes                  |
| ----------- | ------- | ---------------------- |
| id          | INTEGER | PK                     |
| structureId | INTEGER | FK → alt_toc_structure |
| parentId    | INTEGER | nullable, self-ref     |
| textId      | INTEGER | FK → tocText           |
| level       | INTEGER | not null               |
| lineId      | INTEGER | nullable, FK → line    |
| isLastChild | INTEGER | 0/1 bool               |
| hasChildren | INTEGER | 0/1 bool               |

### line_alt_toc

`lineId` + `structureId` composite PK, `altTocEntryId` FK → alt_toc_entry.

### book_acronym

`bookId` + `term` composite PK.

### default_commentator / default_targum

`bookId` + `commentatorBookId`/`targumBookId` composite PK, `position` INTEGER not null.

### bloom_metadata

`id` PK, `chunk_size` INTEGER not null.
