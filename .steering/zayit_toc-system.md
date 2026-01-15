---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**/*toc*|**/vue-zayit/**/*Toc*'
---

# Zayit TOC System

## CRITICAL: Dual TOC Architecture

The system supports TWO separate table of contents sources that must remain independent:

1. **Regular TOC** (`tocEntry` table) - Primary book structure
2. **Alternative TOC** (`alt_toc_entry` table) - Additional navigation structures

**These MUST NOT be mixed** - alt TOC entries should never appear as children of regular TOC entries.

## Database Schema

### Regular TOC
```sql
tocEntry
  - id
  - bookId (direct reference)
  - parentId
  - textId
  - level
  - lineId
  - isLastChild
  - hasChildren
```

### Alternative TOC
```sql
alt_toc_entry
  - id
  - structureId (references alt_toc_structure)
  - parentId
  - textId
  - level
  - lineId
  - isLastChild
  - hasChildren

alt_toc_structure (bridge table)
  - id
  - bookId (connects alt entries to books)
```

**Key Point**: `alt_toc_entry` doesn't know the bookId directly - must JOIN through `alt_toc_structure`.

## SQL Query Pattern

```typescript
getToc: (docId: number) => `
  SELECT DISTINCT
    te.id,
    te.parentId,
    te.textId,
    te.level,
    te.lineId,
    te.isLastChild,
    te.hasChildren,
    tt.text,
    l.lineIndex,
    0 as isAltToc
  FROM tocEntry AS te
  LEFT JOIN tocText AS tt ON te.textId = tt.id
  LEFT JOIN line AS l ON l.id = te.lineId
  WHERE te.bookId = ${docId}
  UNION ALL
  SELECT DISTINCT
    ate.id,
    ate.parentId,
    ate.textId,
    ate.level,
    ate.lineId,
    ate.isLastChild,
    ate.hasChildren,
    tt.text,
    l.lineIndex,
    1 as isAltToc
  FROM alt_toc_entry AS ate
  JOIN alt_toc_structure AS ats ON ats.id = ate.structureId
  LEFT JOIN tocText AS tt ON ate.textId = tt.id
  LEFT JOIN line AS l ON l.id = ate.lineId
  WHERE ats.bookId = ${docId}
`
```

**Critical Elements**:
- `isAltToc` flag (0 or 1) distinguishes the two sources
- Alt TOC joins through `alt_toc_structure` to filter by bookId
- Both queries return identical column structure

## Tree Building Pattern

```typescript
// Separate entries by source
const regularEntries = allEntries.filter(e => !e.isAltToc)
const altEntries = allEntries.filter(e => e.isAltToc)

// Build independent trees
const regularTree = buildTocChildren(undefined, regularEntries)
const altTree = buildTocChildren(undefined, altEntries)

// Wrap alt TOC in synthetic root
if (altTree.length > 0) {
  const altRootNode: TocEntry = {
    id: -1,
    text: 'כותרות נוספות',
    hasChildren: true,
    children: altTree,
    isAltToc: 1,
    // ... other required fields
  }
  tree.push(altRootNode)
}
```

**Key Points**:
- Filter entries by `isAltToc` flag BEFORE building trees
- Build trees separately to prevent cross-contamination
- Alt TOC gets wrapped in synthetic root node labeled "כותרות נוספות"
- Synthetic node uses `id: -1` to avoid conflicts

## Component Indentation Pattern

TOC tree nodes use `depth` prop for indentation, NOT database `level` field:

```vue
<!-- Parent component passes depth=0 for root items -->
<BookTocTreeNode :entry="entry" :depth="0" />

<!-- Child nodes increment depth -->
<BookTocTreeNode :entry="child" :depth="depth + 1" />

<!-- Indentation calculation -->
:style="{ paddingInlineStart: `${20 + depth * 20}px` }"
```

**Why**: Database `level` may not start at 0 or may have gaps. Using `depth` ensures consistent visual hierarchy.

## Common Mistakes

❌ **WRONG**: Using `entry.level` for indentation
```vue
:style="{ paddingInlineStart: `${20 + entry.level * 20}px` }"
```

❌ **WRONG**: Building single tree from mixed entries
```typescript
const tree = buildTocChildren(undefined, allEntries) // Mixes sources!
```

❌ **WRONG**: Filtering alt TOC by structureId directly
```sql
WHERE ate.structureId = ${docId} -- structureId ≠ bookId
```

✅ **CORRECT**: Join through bridge table
```sql
JOIN alt_toc_structure AS ats ON ats.id = ate.structureId
WHERE ats.bookId = ${docId}
```

## Type Definition

```typescript
interface TocEntry {
  id: number
  bookId: number
  parentId?: number
  textId?: number
  level: number
  lineId: number
  lineIndex: number
  isLastChild: boolean
  hasChildren: boolean
  text: string
  isAltToc?: number  // 0 = regular, 1 = alt
  path?: string
  children?: TocEntry[]
  isExpanded?: boolean
}
```
