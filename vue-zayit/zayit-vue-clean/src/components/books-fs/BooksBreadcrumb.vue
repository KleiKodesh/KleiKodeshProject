<script setup lang="ts">
import { IconChevronLeft16Regular, IconHome16Regular } from '@iconify-prerendered/vue-fluent'
import type { CategoryNode } from './booksFsTree'

defineProps<{ path: CategoryNode[] }>()
defineEmits<{ navigate: [index: number] }>()
</script>

<template>
  <nav class="breadcrumb">
    <button class="crumb" :class="{ active: path.length === 1 }" title="איפוס" @click="$emit('navigate', 0)">
      <IconHome16Regular />
    </button>
    <template v-for="(node, i) in path.slice(1)" :key="node.id">
      <IconChevronLeft16Regular class="sep" />
      <button
        class="crumb"
        :class="{ active: i === path.length - 2 }"
        @click="$emit('navigate', i + 1)"
      >{{ node.title }}</button>
    </template>
  </nav>
</template>

<style scoped>
.breadcrumb {
  display: flex;
  align-items: center;
  padding-inline: 4px;
  height: 32px;
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
.crumb:hover { color: var(--text-primary); }
.crumb.active {
  color: var(--text-primary);
  pointer-events: none;
}

.sep {
  color: var(--text-secondary);
  opacity: 0.4;
  flex-shrink: 0;
  width: 12px;
}
</style>
