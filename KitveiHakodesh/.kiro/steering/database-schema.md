# Database Schema Reference

**This file is the single source of truth for the seforim schema.** The same tables were duplicated in the Schema Reference section of `app.md`; that copy has been removed in favour of this one. Do not re-add a second copy anywhere — update this file only.

## Overview

The seforim database is SQLite. All seforim access goes through `src/webview-host/seforimDb.ts` (there is no `db.ts`) — never call fetch against the DB from a component or composable. All raw SQL strings live in `src/webview-host/queries.sql.ts` — no inline SQL anywhere else.

Feature composables call `query()` with a SQL constant from `queries.sql.ts` and a params array. `seforimApi.ts` is the typed layer above it and is preferred for new data access.

**Other databases**: each owns its SQL beside its own access module — `dictionaryDb.ts` + `dictionaryDb.sql.ts`, `userSettingsDb.ts` + `userSettingsDb.sql.ts`. Both the host path and the dev path execute the same SQL string sent from the frontend, so there is nothing to keep in sync between C# and dev for those.

The word-link feature (`wordLinkAnchors.ts` and friends) requires a schema-v2 database with per-word link data. No such table exists in the schema below, which is why that feature is currently dormant.

---

## Reading line text

**Never write `SELECT ... content ... FROM line` again.** The column exists in older
databases and not in newer ones, and where it does exist the value may be a compressed
blob rather than text. Every read goes through the per-backend helper, which supplies both
the SQL fragment and the decode:

| backend                  | helper                                                   |
| ------------------------ | -------------------------------------------------------- |
| KitveiHakodesh.Core      | `SeforimDbSchema` + `SeforimDbLineContentCodec` + `SeforimDbLineContentSql` |
| FtsLib (both legs)       | `SeforimLineContent` - one file, kept identical in both copies |
| KitveiHakodeshService    | FtsLib's `SeforimLineContent` (the service does not reference Core) |
| KitveiHakodeshLib (net48)| `DbAccess` decodes any column named `content` on the way out |
| frontend SQL             | `lineContentColumn` / `lineContentExpression` / `lineContentJoin` in `queries.sql.ts`, flagged by `hasSplitLineContent` from `seforimDb.ts` |

Each exposes the same two pieces: the text EXPRESSION (aliased `content` on both layouts,
so readers above are unchanged) and the JOIN that makes it resolvable. A query that selects
the sidecar without emitting the join fails with `no such column` on schema 6 only - which
is easy to miss when developing against an older library.

### Detect by structure, never by version number

Two independent questions, each answered by looking at the file:

| question              | test                                                        |
| --------------------- | ----------------------------------------------------------- |
| is the content split? | `line_content` exists AND `line` has no `content` column     |
| is it compressed?     | table `zstd_dict` exists                                     |
| are there versions?   | table `version_line` exists (Zayit has none)                 |

`schema_meta.db_schema_version` does carry 1, 2 and 6 for the three shapes, but it is
written by the external library builder and Otzaria itself never reads it. Trusting it
would make us the only consumer that breaks when a rebuild forgets to bump it.

### Decode by the value's runtime type

`NULL` -> null, `string` -> itself, `byte[]` -> decompress. One path covers Zayit, old
Otzaria, schema 6, and a TEXT-declared column holding blobs. Do NOT sniff the zstd magic
bytes and do NOT trust the declared column type. A blob on a database with no dictionary is
an error worth throwing, not something to guess at - it means the wrong database was opened.

### SQL LIKE cannot search compressed text

`WHERE content LIKE '%...%'` compares the stored BYTES, so on a schema-6 database it matches
nothing at all and reports success. Text search goes through the full-text index. The
dictionary feature's headword lookups are served by the `seforim_headword` table in
`Dictionary.db`, built by `build_seforim_headword_index.py` beside it.

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

**Two layouts exist and both must work.** From SeforimLibrary schema 6 on, `line` has NO
`content` column at all - the text moved to the `line_content` sidecar below. A query
written for one layout does not merely return the wrong thing on the other, it fails with
`no such column`.

| column     | type    | notes                                                  |
| ---------- | ------- | ------------------------------------------------------ |
| id         | INTEGER | PK                                                     |
| bookId     | INTEGER | FK -> book                                             |
| lineIndex  | INTEGER | not null                                               |
| content    | TEXT    | not null - **PRE-SCHEMA-6 ONLY**, absent from schema 6 |
| heRef      | TEXT    | nullable                                               |
| tocEntryId | INTEGER | nullable, FK -> tocEntry                               |
| charCount  | INTEGER | not null - visible chars of the DECOMPRESSED text      |

`charCount` stays on `line` in both layouts, so the scrollbar can size itself without
touching - or decompressing - the text.

### line_content (schema 6+)

The line text, split out of `line`. One row per line, same id.

| column  | type    | notes                                              |
| ------- | ------- | -------------------------------------------------- |
| id      | INTEGER | PK, equals `line.id` - so the join is a rowid seek  |
| content | TEXT    | not null; **declared TEXT but holds zstd BLOBs**    |

Note the declared type is a lie by design: SQLite affinity is a hint, not a constraint, so
a `PRAGMA table_info` check reports TEXT and tells you nothing. Branch on the VALUE's
runtime type instead.

### zstd_dict (schema 6+)

The compression dictionaries, shipped inside the database itself.

| column | type    | notes                      |
| ------ | ------- | -------------------------- |
| id     | INTEGER | PK - **not** the frame key |
| dict   | BLOB    | a raw zstd dictionary      |

Frames name the dictionary id that zstd stamped INTO the dictionary (and into each frame
header), not this table's `id`. Load every row and key them by the embedded id. Today the
library ships exactly one, id 32768.

### book_version / version_line

Alternate editions of a book: same structure, different wording per line. Zayit databases
have NEITHER table, so every version-aware query needs a table-existence guard.

`book_version`: `id` PK, `bookId` FK, `versionTitle`, `hasContent` (0/1), plus optional
source/licence/notes columns.

| version_line | type    | notes                                             |
| ------------ | ------- | ------------------------------------------------- |
| versionId    | INTEGER | PK (composite), FK -> book_version                |
| lineId       | INTEGER | PK (composite), FK -> line                        |
| content      | TEXT    | **NULLABLE from schema 6** - see the warning below |
| charCount    | INTEGER | not null                                          |

**`version_line.content` NULL does not mean "empty".** Before schema 6 the column was NOT
NULL, so a NULL could only come from a LEFT JOIN finding no row, and `COALESCE(vl.content,
'')` was correct. From schema 6 it is nullable and NULL means *this edition's text is
identical to the base line*, stored once instead of twice. 744 of 1542 content-carrying
versions are entirely NULL, so that same COALESCE renders each of them as a completely
blank book.

Three cases, and they must be told apart by `vl.lineId IS NULL` rather than collapsed:

| situation            | discriminator          | correct text |
| -------------------- | ---------------------- | ------------ |
| version misses line  | `vl.lineId IS NULL`    | empty        |
| same as base line    | `vl.content IS NULL`   | base text    |
| version has own text | neither                | `vl.content` |

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
