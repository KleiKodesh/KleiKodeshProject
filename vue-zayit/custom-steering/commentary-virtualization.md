---
inclusion: fileMatch
fileMatchPattern: "*CommentaryView*"
---

# Commentary View Virtualization

## Problem

The commentary view displays commentary links grouped by source (e.g., רש״י, תוספות). Each group can contain:

- Very short content (tens of words)
- Very long content (10,000+ words)

This extreme variance in content length makes height estimation for virtual scrolling nearly impossible, especially on iOS with narrow viewports where text wrapping is unpredictable.

## Solution: Flattened Item Structure

Instead of virtualizing entire sections (header + all links), we flatten the structure so each item is virtualized independently:

```typescript
// WRONG: Grouped structure (causes overlapping)
items = [
  {
    type: 'group-with-links',
    groupName: 'רש״י',
    links: [link1, link2, link3, ...] // Could be 10,000+ words total
  }
]

// CORRECT: Flattened structure
items = [
  { type: 'group-header', groupIndex: 0, groupName: 'רש״י' },
  { type: 'link', groupIndex: 0, html: '...' },
  { type: 'link', groupIndex: 0, html: '...' },
  { type: 'link', groupIndex: 0, html: '...' }
]
```

## Implementation

### 1. Virtual Items Structure

```typescript
const virtualCommentaryItems = computed(() => {
  const items: Array<{
    id: string;
    type: "group-header" | "link";
    groupIndex: number;
    groupName?: string;
    targetBookId?: number;
    targetLineIndex?: number;
    html?: string;
  }> = [];

  processedLinkGroups.value.forEach((group, groupIndex) => {
    // Add group header as separate item
    items.push({
      id: `group-header-${groupIndex}`,
      type: "group-header",
      groupIndex: groupIndex,
      groupName: group.groupName,
      targetBookId: group.targetBookId,
      targetLineIndex: group.targetLineIndex,
    });

    // Add each link as separate item
    group.links.forEach((link, linkIndex) => {
      items.push({
        id: `group-${groupIndex}-link-${linkIndex}`,
        type: "link",
        groupIndex: groupIndex,
        html: link.html,
      });
    });
  });

  return items;
});
```

### 2. Template Structure

```vue
<DynamicScrollerItem
  :item="item"
  :active="active"
  :size-dependencies="[item.html?.length || 0, containerWidth]"
>
    <!-- Group Header -->
    <div v-if="item.type === 'group-header'"
         class="bold group-header selectable"
         :data-group-index="item.groupIndex"
         @click="handleGroupClick(item)">
        {{ item.groupName }}
    </div>

    <!-- Individual Commentary Link -->
    <div v-else-if="item.type === 'link'"
         class="selectable line-1.6 justify link-item"
         :data-group-index="item.groupIndex"
         v-html="item.html">
    </div>
</DynamicScrollerItem>
```

### 3. Scroll Tracking

Track based on ANY visible item (header or link) with `data-group-index`:

```typescript
const updateCurrentSection = () => {
  // Look for all items with data-group-index (headers and links)
  const items = scrollerEl.querySelectorAll("[data-group-index]");

  // Find visible items and determine which group is currently shown
  // Updates currentGroupIndex based on what's in viewport
};
```

### 4. Scroll to Group

Scroll to the group header when user selects from combobox:

```typescript
const scrollToGroup = (index: number) => {
  const groupHeaderId = `group-header-${index}`;
  const itemIndex = virtualCommentaryItems.value.findIndex(
    (item) => item.id === groupHeaderId,
  );

  if (itemIndex !== -1) {
    commentaryScrollerRef.value.scrollToItem(itemIndex);
  }
};
```

## Benefits

1. **Accurate height estimation** - DynamicScroller only estimates individual link heights, not entire sections
2. **No overlapping** - Each item is small and manageable, even on iOS narrow viewports
3. **Better scroll tracking** - Updates based on any visible item, not just headers
4. **Better position persistence** - Can track position down to individual items within a group
5. **Responsive combobox** - Updates as you scroll through individual links in long sections

## Key Points

- Each item (header and link) has `groupIndex` to identify which group it belongs to
- `data-group-index` attribute on DOM elements enables scroll tracking
- DynamicScroller measures actual rendered heights and adjusts automatically
- Simple `min-item-size` is sufficient since items are small
- Size dependencies track HTML length and container width for recalculation triggers

## Don't

- Don't try to estimate heights for entire sections with variable content
- Don't wrap multiple links in a single virtual item
- Don't use complex height estimation formulas - let DynamicScroller measure
- Don't track scroll position only by headers - track all items with groupIndex
