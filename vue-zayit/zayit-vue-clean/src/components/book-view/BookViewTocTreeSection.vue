<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { IconChevronLeft20Regular, IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import type { TocEntry } from './useToc'

const props = defineProps<{
  title: string | null
  entries: TocEntry[]
  filter: string
  activeEntryId?: number
  visible?: boolean
}>()

defineEmits<{ select: [entry: TocEntry] }>()

const expanded = ref<Set<number>>(new Set())
const rowRefs = ref<Map<number, HTMLElement>>(new Map())

function setRowRef(el: unknown, id: number) {
  if (el) rowRefs.value.set(id, el as HTMLElement)
  else rowRefs.value.delete(id)
}

watch(() => props.activeEntryId, (id) => {
  if (id == null) return
  const entry = props.entries.find(e => e.id === id)
  if (entry) {
    let current = entry
    while (current.parentId != null) {
      expanded.value.add(current.parentId)
      const parent = entryMap.value.get(current.parentId)
      if (!parent) break
      current = parent
    }
  }
  nextTick(() => scrollActiveIntoView(id))
})

watch(() => props.visible, (val) => {
  if (val && props.activeEntryId != null) nextTick(() => scrollActiveIntoView(props.activeEntryId!))
})

function scrollActiveIntoView(id: number) {
  const el = rowRefs.value.get(id)
  if (!el) return
  const container = el.closest('.entries') as HTMLElement | null
  if (!container) return
  const rowTop = el.offsetTop
  const rowHeight = el.offsetHeight
  const containerHeight = container.clientHeight
  container.scrollTop = rowTop - containerHeight / 2 + rowHeight / 2
}

function toggle(entry: TocEntry) {
  if (expanded.value.has(entry.id)) expanded.value.delete(entry.id)
  else expanded.value.add(entry.id)
}

const entryMap = computed(() => {
  const map = new Map<number, TocEntry>()
  for (const e of props.entries) map.set(e.id, e)
  return map
})

function getPath(entry: TocEntry): string {
  const parts: string[] = []
  let current: TocEntry | undefined = entry
  while (current) {
    parts.unshift(current.text)
    current = current.parentId != null ? entryMap.value.get(current.parentId) : undefined
  }
  return parts.join(' / ')
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

</script>

<template>
  <div class="toc-section">
    <div v-if="title" class="section-title">{{ title }}</div>
    <div class="entries">
      <div
        v-for="entry in visibleEntries"
        :key="entry.id"
        :ref="el => setRowRef(el, entry.id)"
        class="toc-row"
        :class="{ 'is-filtered': !!filter, 'is-active': entry.id === activeEntryId }"
      >
        <div
          v-if="entry.hasChildren && !filter"
          class="chevron-btn"
          :style="{ marginInlineStart: `${entry.level * 10}px` }"
          @click.stop="toggle(entry)"
        >
          <IconChevronDown20Regular v-if="expanded.has(entry.id)" />
          <IconChevronLeft20Regular v-else />
        </div>
        <span
          v-else-if="!filter"
          class="chevron-placeholder"
          :style="{ marginInlineStart: `${entry.level * 10}px` }"
        />
        <div
          class="toc-entry"
          @click.stop="$emit('select', entry)"
        >{{ filter ? getPath(entry) : entry.text }}</div>
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
}
.toc-row:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.toc-row:has(.toc-entry:active) { background: color-mix(in srgb, var(--text-primary) 10%, transparent); }
.toc-row.is-active { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }
.toc-row.is-active .toc-entry { color: var(--accent-color, #3478f6); font-weight: 500; }

.chevron-btn {
  width: 24px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 32px;
  color: var(--text-secondary);
  cursor: pointer;
}

.chevron-placeholder {
  width: 20px;
  flex-shrink: 0;
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
  cursor: pointer;
}

.toc-row.is-filtered .toc-entry {
  height: auto;
  line-height: 1.4;
  padding-block: 6px;
  white-space: normal;
  word-break: break-word;
}

.toc-row.is-filtered {
  height: auto;
  align-items: flex-start;
  padding-inline-start: 12px;
}
</style>
