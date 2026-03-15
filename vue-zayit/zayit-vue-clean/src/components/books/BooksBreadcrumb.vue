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
  gap: 2px;
  padding: 6px 12px;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-secondary);
  min-height: 40px;
}

.crumb {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 6px;
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 0.1s;
}
.crumb:hover { background: var(--hover-bg); color: var(--text-primary); }
.crumb.active { color: var(--text-primary); font-weight: 500; cursor: default; pointer-events: none; }

.sep { color: var(--text-secondary); opacity: 0.5; }
</style>
