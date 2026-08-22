<script setup lang="ts">
import BookCatalogBreadcrumbChevronDropdown from './BookCatalogBreadcrumbChevronDropdown.vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

defineProps<{ path: CategoryNode[] }>()
defineEmits<{ navigate: [number]; navigateToSibling: [{ atIndex: number; node: CategoryNode }] }>()
</script>

<template>
  <nav class="breadcrumb">
    <!-- Root's dropdown, leading the crumbs: lists the root's children, so it is
         how the user jumps sideways from the top level. (Home itself is the
         pill's leading cap, in BookCatalogTitleBar.) -->
    <BookCatalogBreadcrumbChevronDropdown
      :parent-node="path[0]!"
      :active-child-id="path.length > 1 ? path[1]!.id : -1"
      @select="$emit('navigateToSibling', { atIndex: 1, node: $event })"
    />

    <!-- Remaining crumbs (skip index 0 which is root/home) -->
    <template v-for="(node, i) in path.slice(1)" :key="node.id">
      <button
        class="crumb"
        :class="{ active: i === path.length - 2 }"
        @click.stop="$emit('navigate', i + 1)"
      >
        {{ node.title }}
      </button>

      <!-- Separator after each non-last crumb: lists that crumb's children -->
      <BookCatalogBreadcrumbChevronDropdown
        v-if="i < path.length - 2"
        :parent-node="node"
        :active-child-id="path[i + 2]!.id"
        @select="$emit('navigateToSibling', { atIndex: i + 2, node: $event })"
      />
    </template>
  </nav>
</template>

<style scoped>
.breadcrumb {
  display: flex;
  align-items: center;
  /* Fills the address bar's middle. The slack a short path leaves over is the
     bar's click target — clicking it opens the field — so this must not also
     carry a margin or a width cap eating into it. Shrinks and scrolls rather than
     widening the pill, so a deep path never pushes the buttons off its ends. */
  flex: 1 1 auto;
  min-width: 0;
  height: 100%;
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
}
.breadcrumb::-webkit-scrollbar {
  display: none;
}
.crumb {
  display: inline-flex;
  align-items: center;
  padding: 0 8px;
  /* Sized to the pill it sits in rather than to the toolbar button height: the
     address bar is 30px inside its border, so a 32px crumb would not fit. */
  height: 22px;
  border-radius: 4px;
  font-size: 13px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
}
.crumb:hover {
  color: var(--text-primary);
}
.crumb.active {
  color: var(--text-primary);
  pointer-events: none;
}
</style>
