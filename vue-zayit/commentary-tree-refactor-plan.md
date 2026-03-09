# Commentary Tree Refactor Plan

## Goal

Simplify commentary tree structure by using category-based period detection instead of connection types.

## Current Structure

```
- מקור (SOURCE)
- תרגומים (TARGUM)
- מפרשים (COMMENTARY)
  - חז"ל
  - ראשונים
  - אחרונים
  - קבלה
  - מוסר וחסידות
  - הלכה
  - אחר
- קשרים (REFERENCE)
- שונות (OTHER)
```

## New Structure

```
- מקור (SOURCE) - separate node
- תרגומים (TARGUM) - separate node
- גאונים - period node (contains מפרשים, קשרים, שונות)
- ראשונים - period node (contains מפרשים, קשרים, שונות)
- אחרונים - period node (contains מפרשים, קשרים, שונות)
- [Root Category Nodes] - for books without meaningful periods
  - תנ"ך
  - הלכה
  - קבלה
  - מוסר וחסידות
  - etc.
```

## Implementation Steps

### Step 1: Update categoryTreeStore.ts

**File:** `zayit-vue/src/data/stores/categoryTreeStore.ts`

**Changes to `findCategoryPeriod` function:**

```typescript
// Current: Returns hardcoded periods based on category title matching
// New:
// 1. Look for meaningful periods: גאונים, ראשונים, אחרונים
// 2. If found, return that period
// 3. If not found, return the root (first-tier) category title
```

**Logic:**

- Traverse up category tree from book's category
- Check each category title for: גאונים, ראשונים, אחרונים
- If match found → return that period name
- If no match → find root category and return its title
- Assign result to `book.period`

### Step 2: Update useCommentaryTree.ts

**File:** `zayit-vue/src/components/commentary/useCommentaryTree.ts`

**Changes:**

1. Remove connection type grouping for מפרשים, קשרים, שונות
2. Keep מקור and תרגומים as separate connection type nodes
3. Group מפרשים, קשרים, שונות by `book.period` (pre-calculated)
4. Create period/category nodes dynamically based on `book.period` values
5. Sort books alphabetically within each node

**New tree building logic:**

```typescript
// 1. Handle מקור - create connection type node
// 2. Handle תרגומים - create connection type node
// 3. Handle מפרשים, קשרים, שונות together:
//    - Group by book.period
//    - Create period nodes (גאונים, ראשונים, אחרונים)
//    - Create category nodes (for non-period books)
//    - Sort books alphabetically within each node
```

### Step 3: Update CommentaryTreeNode type

**File:** `zayit-vue/src/components/commentary/useCommentaryTree.ts`

**Changes:**

```typescript
export interface CommentaryTreeNode {
  type: "connection-type" | "period" | "category" | "book"; // Add 'category' type
  name: string;
  hebrewName: string;
  connectionType?: string;
  period?: string;
  category?: string; // Add category field
  bookId?: number;
  lineIndex?: number;
  children: CommentaryTreeNode[];
  path: string[];
  metadata?: CommentaryMetadata;
}
```

## Benefits

1. **Simpler structure** - No nested connection type → period hierarchy
2. **Automatic merging** - "ראשונים על תורה" + "ראשונים על נך" → single "ראשונים" node
3. **Flexible fallback** - Books without periods use meaningful category nodes
4. **Performance** - Period detection happens once at startup
5. **Cleaner code** - Less hardcoded logic, more data-driven

## Testing Checklist

- [ ] מקור books appear in separate node
- [ ] תרגומים books appear in separate node
- [ ] Books from different categories merge into same period node (e.g., ראשונים)
- [ ] Books without periods appear under root category nodes
- [ ] Books sorted alphabetically within each node
- [ ] Tree expands/collapses correctly
- [ ] Selected book highlights correctly
- [ ] Navigation works properly
