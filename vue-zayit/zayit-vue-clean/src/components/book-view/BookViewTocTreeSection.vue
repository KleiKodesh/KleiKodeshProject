<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronLeft20Regular, IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { TocEntry } from './useToc'

const props = defineProps<{
  title: string | null
  entries: TocEntry[]
  filter: string
}>()

defineEmits<{ select: [entry: TocEntry] }>()

const expanded = ref<Set<number>>(new Set())

function toggle(entry: TocEntry) {
  if (expanded.value.has(entry.id)) expanded.value.delete(entry.id)
  else expanded.value.add(entry.id)
}

const visibleEntries = computed(() => {
  if (props.filter) return props.entries.filter(e => e.text.includes(props.filter))

  const result: TocEntry[] = []
  const hidden = new Set<number>()

  for (const entry of props.entries) {
    if (entry.parentId !== null && hidden.has(entry.parentId)) {
      hidden.add(entry.id)
      continue
    }
    result.push(entry)
    if (entry.hasChildren && !expanded.value.has(entry.id)) hidden.add(entry.id)
  }
  return result
})

// Keys of groups where at least one sibling has children — used to align leaf nodes
const siblingsWithChildren = computed(() => {
  const set = new Set<string>()
  for (const e of props.entries) {
    if (e.hasChildren) set.add(`${e.parentId ?? 'root'}-${e.level}`)
  }
  return set
})
</script>

<template>
  <div class="toc-section">
    <div v-if="title" class="section-title">{{ title }}</div>
    <div class="entries">
      <div v-for="entry in visibleEntries" :key="entry.id" class="toc-row">
        <div
          v-if="entry.hasChildren && !filter"
          class="chevron-btn"
          @click.stop="toggle(entry)"
        >
          <IconChevronDown20Regular v-if="expanded.has(entry.id)" />
          <IconChevronLeft20Regular v-else />
        </div>
        <span
          v-else-if="siblingsWithChildren.has(`${entry.parentId ?? 'root'}-${entry.level}`)"
          class="chevron-placeholder"
        />
        <div
          class="toc-entry"
          :style="{ paddingInlineStart: `${entry.level * 16 + 4}px` }"
          @click.stop="$emit('select', entry)"
        >{{ entry.text }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.toc-section {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 120px;
  flex: 1;
}

.section-title {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-secondary);
  padding: 6px 12px 4px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.entries {
  flex: 1;
  overflow: auto;
  min-height: 0;
}

.toc-row {
  display: flex;
  align-items: center;
  height: 32px;
  min-width: 100%;
  padding-inline-end: 8px;
  cursor: pointer;
}
.toc-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.toc-row:active { background: color-mix(in srgb, var(--text-primary) 10%, transparent); }

.chevron-btn, .chevron-placeholder {
  width: 24px;
  flex-shrink: 0;
}

.chevron-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 32px;
  color: var(--text-secondary);
}

.toc-entry {
  flex: 1;
  min-width: 0;
  height: 32px;
  line-height: 32px;
  padding-inline-end: 4px;
  font-size: 0.85rem;
  color: var(--text-primary);
  white-space: nowrap;
}
</style>
