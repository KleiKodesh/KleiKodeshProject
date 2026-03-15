<script setup lang="ts">
import { IconHome20Regular } from '@iconify-prerendered/vue-fluent'
import type { CategoryNode } from './booksFsTree'

defineProps<{ path: CategoryNode[] }>()
defineEmits<{ navigate: [index: number] }>()
</script>

<template>
  <nav class="breadcrumb">
    <button class="crumb" :class="{ active: path.length === 1 }" @click="$emit('navigate', 0)">
      <IconHome20Regular />
    </button>
    <template v-if="path.length > 1">
      <span class="sep">/</span>
      <template v-for="(node, i) in path.slice(1)" :key="node.id">
        <button class="crumb" :class="{ active: i === path.length - 2 }" @click="$emit('navigate', i + 1)">
          {{ node.title }}
        </button>
        <span v-if="i < path.length - 2" class="sep">/</span>
      </template>
    </template>
  </nav>
</template>

<style scoped>
.breadcrumb {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0;
  padding: 2px 8px;
  background: var(--bg-secondary);
  min-height: 32px;
}

.crumb {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 2px 4px;
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  color: var(--accent-color);
  cursor: pointer;
  transition: background 150ms;
  min-height: 28px;
}
.crumb:hover { background: var(--hover-bg); }
.crumb:active { background: var(--active-bg); }
.crumb.active { color: var(--text-primary); font-weight: 600; cursor: default; pointer-events: none; }
.crumb svg { color: var(--accent-color); }

.sep { color: var(--text-secondary); opacity: 0.4; font-size: 12px; padding: 0 1px; }
</style>
