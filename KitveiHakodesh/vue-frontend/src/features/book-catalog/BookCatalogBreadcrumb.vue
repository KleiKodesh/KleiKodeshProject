<script setup lang="ts">
import { IconHome16Regular } from '@iconify-prerendered/vue-fluent'
import BookCatalogBreadcrumbChevronDropdown from './BookCatalogBreadcrumbChevronDropdown.vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

defineProps<{ path: CategoryNode[] }>()
defineEmits<{ navigate: [number]; navigateToSibling: [{ atIndex: number; node: CategoryNode }] }>()
</script>

<template>
  <nav class="breadcrumb">
    <!-- Home crumb — its dropdown lists root-level children (path[0].children) -->
    <button
      class="crumb"
      :class="{ active: path.length === 1 }"
      title="איפוס"
      @click="$emit('navigate', 0)"
    >
      <IconHome16Regular />
    </button>

    <!-- Separator after home: lists root children so user can jump sideways from root -->
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
        @click="$emit('navigate', i + 1)"
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
  padding-inline: 4px;
  /* Fill the toolbar height (set by the parent .titlebar) rather than a fixed
     value, so the catalog toolbar matches the BookView toolbar in every density. */
  height: 100%;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.crumb {
  display: inline-flex;
  align-items: center;
  padding: 0 5px;
  height: 22px;
  border-radius: 3px;
  font-size: 12px;
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
