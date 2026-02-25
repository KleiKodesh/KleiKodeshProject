# scrollIntoView() jumps to wrong position in Vite development mode

## Description

When using native DOM `scrollIntoView()` in development mode with Vite, the scroll position jumps to the wrong location. The element does not end up where it should be in the viewport.

This issue only occurs in development mode with Vite dev server. Production builds work correctly.

## Steps to Reproduce

1. Set up a Vue 3 project with Vite dev server
2. Create a scrollable tree component with nested elements
3. Use `element.scrollIntoView({ behavior: 'auto', block: 'center' })` to scroll to a specific node
4. Call the scroll function from user interaction (e.g., clicking a navigation item)
5. Observe that the element scrolls to an incorrect position

## Expected Behavior

Calling `element.scrollIntoView({ behavior: 'auto', block: 'center' })` should scroll the element to the center of the viewport.

## Actual Behavior

In development mode, the element scrolls to the wrong position - often significantly off from where it should be.

## Real-World Example (TOC Tree)

```typescript
// This code exhibits the sync issue in dev mode
function scrollToCurrentTocEntry(tocEntry: TocEntry) {
  nextTick(() => {
    const treeEl = (treeRef.value as any)?.$el || treeRef.value;
    if (treeEl) {
      // Find the DOM element for this TOC entry
      const allNodes = treeEl.querySelectorAll('[role="treeitem"]');
      for (const node of allNodes) {
        const nodeText = node.querySelector(".node-title")?.textContent;
        if (nodeText === tocEntry.text) {
          // Highlight the node
          const treeNodeDiv = node.querySelector(".tree-node") as HTMLElement;
          if (treeNodeDiv) {
            treeNodeDiv.classList.add("highlight-current");
          }

          // Scroll to the node - JUMPS TO WRONG POSITION IN DEV MODE
          node.scrollIntoView({ behavior: "auto", block: "center" });
          break;
        }
      }
    }
  });
}
```

## Environment

- **Vue version**: 3.5.22
- **Build tool**: Vite 7.1.11
- **Browser**: Tested on multiple browsers
- **OS**: Windows
- **Mode**: Development mode only (production builds work correctly)

## Minimal Reproduction

```vue
<template>
  <div
    class="tree-container"
    ref="containerRef"
    style="height: 400px; overflow-y: auto;"
  >
    <div
      v-for="item in items"
      :key="item.id"
      class="tree-item"
      :data-id="item.id"
    >
      {{ item.text }}
    </div>
  </div>
  <button @click="scrollToItem(50)">Scroll to Item 50</button>
</template>

<script setup lang="ts">
import { ref, nextTick } from "vue";

const containerRef = ref<HTMLElement | null>(null);

const items = Array.from({ length: 100 }, (_, i) => ({
  id: i,
  text: `Item ${i}`,
}));

// This function exhibits the sync issue in dev mode
function scrollToItem(itemId: number) {
  nextTick(() => {
    const container = containerRef.value;
    if (!container) return;

    const element = container.querySelector(`[data-id="${itemId}"]`);
    if (element) {
      // JUMPS TO WRONG POSITION IN DEV MODE
      element.scrollIntoView({ behavior: "auto", block: "center" });
    }
  });
}
</script>
```

## Additional Context

- Production builds (using `vite build`) do not exhibit this issue
- The problem appears consistently across different browsers
- Possible causes:
  - Vite's HMR (Hot Module Replacement) interfering with scroll calculations
  - Dev server's module transformation affecting DOM measurement timing
  - Source map generation causing timing issues with layout calculations

## Questions

1. Is this a known issue with Vite's development mode affecting scroll operations?
2. Are there Vite configuration options that could resolve this?
3. What is the recommended approach to handle scrollIntoView in Vite dev mode?
