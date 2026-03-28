<script setup lang="ts">
import { IconChevronLeft16Regular, IconChevronDown16Regular } from '@iconify-prerendered/vue-fluent'

export interface TreeNodeItem {
  id: number
  parentId: number | null
  level: number
  hasChildren: boolean | number
  text: string
}

const props = defineProps<{
  node: TreeNodeItem
  expanded: boolean
  active: boolean
  focused: boolean
  filtered: boolean
  indent?: number
  rowHeight?: number
  fontSize?: string
}>()

defineEmits<{ toggle: []; select: [] }>()

const indentPx = () => `${props.node.level * (props.indent ?? 10)}px`
const rh = () => `${props.rowHeight ?? 28}px`
const fs = () => props.fontSize ?? '0.8rem'
</script>

<template>
  <div
    class="tree-row"
    data-nav-item
    :style="{ '--rh': rh(), '--fs': fs() }"
    :class="{ 'is-active': active, 'is-focused': focused, 'is-filtered': filtered, 'is-parent': node.hasChildren && !filtered }"
    @keydown.space.prevent="node.hasChildren && !filtered ? $emit('toggle') : $emit('select')"
  >
    <div
      v-if="node.hasChildren && !filtered"
      class="chevron-btn"
      :style="{ marginInlineStart: indentPx() }"
      @click.stop="$emit('toggle')"
    >
      <IconChevronDown16Regular v-if="expanded" />
      <IconChevronLeft16Regular v-else />
    </div>
    <span
      v-else-if="!filtered"
      class="chevron-placeholder"
      :style="{ marginInlineStart: indentPx() }"
    />
    <div class="tree-label" @click.stop="$emit('select')">
      <slot>{{ node.text }}</slot>
    </div>
  </div>
</template>

<style scoped>
.tree-row {
  display: flex;
  align-items: center;
  height: var(--rh, 28px);
  min-width: 100%;
  padding-inline-end: 8px;
}
.tree-row.is-parent {
  position: sticky;
  top: calc(v-bind('node.level') * var(--rh, 28px));
  z-index: calc(10 - v-bind('node.level'));
  background: var(--tree-bg, var(--bg-primary));
}
.tree-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.tree-row.is-parent:hover { background: color-mix(in srgb, var(--text-primary) 6%, var(--tree-bg, var(--bg-primary))); }
.tree-row:has(.tree-label:active) { background: color-mix(in srgb, var(--text-primary) 10%, transparent); }
.tree-row.is-parent:has(.tree-label:active) { background: color-mix(in srgb, var(--text-primary) 10%, var(--tree-bg, var(--bg-primary))); }
.tree-row.is-active { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }
.tree-row.is-parent.is-active { background: color-mix(in srgb, var(--text-primary) 8%, var(--tree-bg, var(--bg-primary))); }
.tree-row.is-active .tree-label { color: var(--accent-color, #3478f6); font-weight: 500; }

.chevron-btn {
  width: 24px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  height: var(--rh, 28px);
  color: var(--text-secondary);
  cursor: pointer;
}

.chevron-placeholder {
  width: 20px;
  flex-shrink: 0;
}

.tree-label {
  flex: 1;
  min-width: 0;
  height: var(--rh, 28px);
  line-height: var(--rh, 28px);
  padding-inline-end: 4px;
  font-size: var(--fs, 0.8rem);
  color: var(--text-primary);
  white-space: nowrap;
  cursor: pointer;
}

.tree-row.is-filtered {
  height: auto;
  align-items: flex-start;
  padding-inline-start: 12px;
}
.tree-row.is-filtered .tree-label {
  height: auto;
  line-height: 1.4;
  padding-block: 8px;
  white-space: normal;
  word-break: break-word;
}
</style>
