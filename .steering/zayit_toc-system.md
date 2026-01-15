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

## TOC Overlay Pattern (BookViewPage)

### CRITICAL: Overlay Architecture for Instant Navigation

The TOC uses an **overlay pattern** to enable instant navigation even in very long books:

```vue
<template>
  <div class="book-view-wrapper">
    <!-- TOC as overlay with keep-alive -->
    <keep-alive>
      <BookTocTreeView v-if="isTocOpen"
                       class="toc-overlay"
                       @select-line="handleTocSelection" />
    </keep-alive>

    <!-- BookLineViewer stays mounted underneath -->
    <SplitPane>
      <template #top>
        <BookLineViewer ref="lineViewerRef" />
      </template>
    </SplitPane>
  </div>
</template>

<style scoped>
.book-view-wrapper {
  position: relative;
}

.toc-overlay {
  position: absolute;
  height: 100%;
  width: 100%;
  z-index: 100;
}
</style>
```

**Why This Pattern Works**:
1. **BookLineViewer stays mounted** - All placeholders render immediately, virtualization starts loading
2. **TOC appears as overlay** - BookLineViewer continues working underneath
3. **Instant navigation** - When user clicks TOC entry, BookLineViewer is already there and ready
4. **State preservation** - `<keep-alive>` preserves TOC search input and expanded nodes

**Navigation Flow**:
```typescript
// BookTocTreeView emits selectLine
handleSelectLine(lineIndex: number) {
  emit('selectLine', lineIndex)
  tabStore.closeToc()  // Close overlay
}

// BookViewPage forwards to BookLineViewer
handleTocSelection(lineIndex: number) {
  lineViewerRef.value?.handleTocSelection(lineIndex)
}

// BookLineViewer navigates immediately (already mounted with placeholders)
async handleTocSelection(lineIndex: number) {
  await viewerState.handleTocSelection(lineIndex)
  // Scroll immediately, load visible lines with priority
}
```

**Common Mistakes**:

❌ **WRONG**: Using `<component :is>` to switch between views
```vue
<!-- This unmounts BookLineViewer, breaking virtualization -->
<component :is="isTocOpen ? BookTocTreeView : BookLineViewer" />
```

❌ **WRONG**: Conditional rendering of BookLineViewer
```vue
<!-- This destroys all loaded content when TOC opens -->
<BookLineViewer v-if="!isTocOpen" />
```

✅ **CORRECT**: Overlay pattern with both components mounted
```vue
<BookTocTreeView v-if="isTocOpen" class="toc-overlay" />
<BookLineViewer /> <!-- Always mounted -->
```

## Alt TOC Line Display System

### Efficient Lookup Map
Create Map-based lookup for O(1) line-to-TOC access:

```typescript
// Build alt TOC lookup map supporting multiple entries per line
const altTocByLineIndex = new Map<number, AltTocLineEntry[]>()
altEntries.forEach(entry => {
  if (entry.lineIndex !== undefined && entry.text) {
    const lineIndex = entry.lineIndex
    const altTocEntry: AltTocLineEntry = {
      text: entry.text,
      level: entry.level,
      lineIndex: entry.lineIndex
    }
    
    if (!altTocByLineIndex.has(lineIndex)) {
      altTocByLineIndex.set(lineIndex, [])
    }
    altTocByLineIndex.get(lineIndex)!.push(altTocEntry)
  }
})
```

### Component Integration Pattern
```vue
<!-- BookViewPage: Load TOC data once, distribute to components -->
<BookTocTreeView :toc-entries="tocEntries" />
<BookLineViewer :alt-toc-by-line-index="altTocByLineIndex" />

<!-- BookLineViewer: Pass entries to individual lines -->
<BookLine :alt-toc-entries="altTocByLineIndex?.get(lineIndex)" />

<!-- BookLine: Render as semantic HTML headings -->
<component v-for="entry in altTocEntries"
           :is="getHeadingTag(entry.level)"
           class="alt-toc-entry"
           v-html="entry.text">
</component>
```

### Toggle Functionality
Store toggle state in tab store for persistence:

```typescript
// Tab.ts
interface BookState {
  showAltToc?: boolean  // Default: true
}

// TabHeader.vue dropdown
<button @click="toggleAltToc">
  {{ myTab?.bookState?.showAltToc !== false ? 'הסתר' : 'הצג' }} כותרות נוספות
</button>
```

**Key Points**:
- Default to showing alt TOC entries (`!== false` check)
- State persists per tab, not globally
- Toggle affects all lines simultaneously
