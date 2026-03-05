# Database Schema

## Core Tables

### category

- `id` INTEGER PRIMARY KEY
- `parentId` INTEGER
- `title` TEXT NOT NULL
- `level` INTEGER NOT NULL DEFAULT 0
- `orderIndex` INTEGER NOT NULL DEFAULT 999

### book

- `id` INTEGER PRIMARY KEY
- `categoryId` INTEGER NOT NULL
- `sourceId` INTEGER NOT NULL
- `title` TEXT NOT NULL
- `heShortDesc` TEXT
- `notesContent` TEXT
- `orderIndex` INTEGER NOT NULL DEFAULT 999
- `totalLines` INTEGER NOT NULL DEFAULT 0
- `isBaseBook` INTEGER NOT NULL DEFAULT 0
- `hasTargumConnection` INTEGER NOT NULL DEFAULT 0
- `hasReferenceConnection` INTEGER NOT NULL DEFAULT 0
- `hasSourceConnection` INTEGER NOT NULL DEFAULT 0
- `hasCommentaryConnection` INTEGER NOT NULL DEFAULT 0
- `hasOtherConnection` INTEGER NOT NULL DEFAULT 0
- `hasAltStructures` INTEGER NOT NULL DEFAULT 0
- `hasTeamim` INTEGER NOT NULL DEFAULT 0
- `hasNekudot` INTEGER NOT NULL DEFAULT 0
- `externalLibraryId` INTEGER

### line

- `id` INTEGER PRIMARY KEY
- `bookId` INTEGER NOT NULL
- `lineIndex` INTEGER NOT NULL
- `content` TEXT NOT NULL
- `heRef` TEXT
- `tocEntryId` INTEGER
- `chunk_id` INTEGER

### link

- `id` INTEGER PRIMARY KEY
- `sourceBookId` INTEGER NOT NULL
- `targetBookId` INTEGER NOT NULL
- `sourceLineId` INTEGER NOT NULL
- `targetLineId` INTEGER NOT NULL
- `connectionTypeId` INTEGER NOT NULL

## Metadata Tables

### author

- `id` INTEGER PRIMARY KEY
- `name` TEXT NOT NULL

### topic

- `id` INTEGER PRIMARY KEY
- `name` TEXT NOT NULL

### pub_place

- `id` INTEGER PRIMARY KEY
- `name` TEXT NOT NULL

### pub_date

- `id` INTEGER PRIMARY KEY
- `date` TEXT NOT NULL

### source

- `id` INTEGER PRIMARY KEY
- `name` TEXT NOT NULL

### connection_type

- `id` INTEGER PRIMARY KEY
- `name` TEXT NOT NULL

## Junction Tables

### book_pub_date

- `bookId` INTEGER PRIMARY KEY
- `pubDateId` INTEGER PRIMARY KEY

### book_pub_place

- `bookId` INTEGER PRIMARY KEY
- `pubPlaceId` INTEGER PRIMARY KEY

### book_topic

- `bookId` INTEGER PRIMARY KEY
- `topicId` INTEGER PRIMARY KEY

### book_author

- `bookId` INTEGER PRIMARY KEY
- `authorId` INTEGER PRIMARY KEY

### book_acronym

- `bookId` INTEGER PRIMARY KEY
- `term` TEXT PRIMARY KEY

## TOC Tables

### tocText

- `id` INTEGER PRIMARY KEY
- `text` TEXT NOT NULL

### tocEntry

- `id` INTEGER PRIMARY KEY
- `bookId` INTEGER NOT NULL
- `parentId` INTEGER
- `textId` INTEGER NOT NULL
- `level` INTEGER NOT NULL
- `lineId` INTEGER
- `isLastChild` INTEGER NOT NULL DEFAULT 0
- `hasChildren` INTEGER NOT NULL DEFAULT 0

### line_toc

- `lineId` INTEGER PRIMARY KEY
- `tocEntryId` INTEGER NOT NULL

### alt_toc_structure

- `id` INTEGER PRIMARY KEY
- `bookId` INTEGER NOT NULL
- `key` TEXT NOT NULL
- `title` TEXT
- `heTitle` TEXT

### alt_toc_entry

- `id` INTEGER PRIMARY KEY
- `structureId` INTEGER NOT NULL
- `parentId` INTEGER
- `textId` INTEGER NOT NULL
- `level` INTEGER NOT NULL
- `lineId` INTEGER
- `isLastChild` INTEGER NOT NULL DEFAULT 0
- `hasChildren` INTEGER NOT NULL DEFAULT 0

### line_alt_toc

- `lineId` INTEGER PRIMARY KEY
- `structureId` INTEGER PRIMARY KEY
- `altTocEntryId` INTEGER NOT NULL

## Default Commentators/Targum

### default_commentator

- `bookId` INTEGER PRIMARY KEY
- `commentatorBookId` INTEGER PRIMARY KEY
- `position` INTEGER NOT NULL

### default_targum

- `bookId` INTEGER PRIMARY KEY
- `targumBookId` INTEGER PRIMARY KEY
- `position` INTEGER NOT NULL

## Other Tables

### category_closure

- `ancestorId` INTEGER PRIMARY KEY
- `descendantId` INTEGER PRIMARY KEY

### book_has_links

- `bookId` INTEGER PRIMARY KEY
- `hasSourceLinks` INTEGER NOT NULL DEFAULT 0
- `hasTargetLinks` INTEGER NOT NULL DEFAULT 0

### bloom_metadata

- `id` INTEGER PRIMARY KEY
- `chunk_size` INTEGER NOT NULL

### sqlite_sequence

- `name` TEXT
- `seq` INTEGER
