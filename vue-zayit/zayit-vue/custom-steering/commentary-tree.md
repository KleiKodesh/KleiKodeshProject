---
inclusion: manual
---

# Commentary Tree Structure

Commentary tree groups books by connection type and category for organized navigation.

## Tree Order

1. מקור (SOURCE) - flat list, alphabetically sorted
2. קשרים (REFERENCE) - flat list, alphabetically sorted
3. תרגומים (TARGUM) - flat list, alphabetically sorted
4. מפרשים (COMMENTARY) - grouped by categories with "מפרשים - " prefix
5. שונות (OTHER) - grouped by categories with "שונות - " prefix

## Category Grouping Logic (for מפרשים and שונות)

Books are grouped using this priority order:

1. Use period if available (גאונים, ראשונים, אחרונים)
2. For תנ"ך/משנה/תלמוד books without period, use secondaryCategory
3. Otherwise use rootCategory

## Category Sorting

Categories are sorted by:

1. Hardcoded periods first (גאונים, ראשונים, אחרונים) in that order
2. Other categories follow, sorted by category tree orderIndex
3. Books within each category sorted alphabetically in Hebrew

## Implementation Files

- `useCommentaryTree.ts` - builds the tree structure
- `CommentaryTreeView.vue` - displays the tree
- `CommentaryTreeViewNode.vue` - recursive tree node component
- Book type includes: period, rootCategory, secondaryCategory, rootCategoryOrder, secondaryCategoryOrder

## Header Display

Commentary headers show: "category > book name" (e.g., "מפרשים - ראשונים > רש"י")
