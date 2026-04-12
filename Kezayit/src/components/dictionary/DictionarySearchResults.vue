<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { IconOpen20Regular } from '@iconify-prerendered/vue-fluent'
import type { DictEntry, DictEntryContent } from './useDictionarySearch'
import DictionaryEntryView from './DictionaryEntryView.vue'

const props = defineProps<{
  results: DictEntry[]
  filteredResults: DictEntry[]
  searching: boolean
  hasSearched: boolean
  activeSource: Set<string>
  bookCounts: Map<string, { count: number }>
  expandedEntries: Map<number, DictEntryContent | null>
}>()

const emit = defineEmits<{
  toggleSource: [src: string]
  clearSources: []
  toggle: [entry: DictEntry]
  openInViewer: [entryId: number]
}>()

const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.filteredResults.length,
    getScrollElement: () => scrollEl.value,
    estimateSize: () => 72,
    overscan: 5,
    measureElement: (el: Element) => el.getBoundingClientRect().height,
  })),
)

useVirtualScrollerKeys(
  scrollEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.filteredResults.length,
)

watch(
  () => props.expandedEntries,
  () => virtualizer.value.measure(),
  { deep: true },
)

/** Type label shown as a badge */
function typeLabel(entry: DictEntry): string {
  if (entry.type === 'abbrev') return 'ר"ת'
  if (entry.type === 'wiktionary') return 'ויקי'
  if (entry.type === 'book') return ''
  return 'ארמית'
}

/** First sense preview text (for book entries shown before expand) */
function bookPreview(entry: DictEntry): string {
  const raw = entry.definition ?? ''
  // Strip HTML tags for preview
  const plain = raw
    .replace(/<[^>]+>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
  return plain.length > 120 ? plain.slice(0, 120) + '…' : plain
}

/** Whether this entry has inline content to show (non-book types) */
function hasInlineContent(entry: DictEntry): boolean {
  return entry.type !== 'book'
}
</script>

<template>
  <div class="results-wrap">
    <!-- Filter tabs -->
    <div v-if="results.length > 0" class="dict-filter-bar">
      <button
        class="dict-filter-tab"
        :class="{ active: activeSource.size === 0 }"
        @click="emit('clearSources')"
      >
        הכל <span class="dict-filter-count">{{ results.length }}</span>
      </button>
      <button
        v-for="[src, info] in bookCounts"
        :key="src"
        class="dict-filter-tab"
        :class="{ active: activeSource.has(src) }"
        @click="emit('toggleSource', src)"
      >
        {{ src }}
        <span class="dict-filter-count">{{ info.count }}</span>
      </button>
    </div>

    <!-- States -->
    <div v-if="searching" class="dict-state-msg">מחפש...</div>
    <div v-else-if="hasSearched && filteredResults.length === 0" class="dict-state-msg">
      לא נמצאו תוצאות
    </div>

    <!-- Results list -->
    <div v-else ref="scrollEl" class="dict-scroll" tabindex="0">
      <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
        <div
          v-for="vRow in virtualizer.getVirtualItems()"
          :key="String(vRow.key)"
          :ref="(el) => el && virtualizer.measureElement(el as Element)"
          :data-index="vRow.index"
          :style="{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            transform: `translateY(${vRow.start}px)`,
          }"
        >
          <div class="dict-card">
            <!-- Card header: headword + badges + open button -->
            <div class="dict-card-header">
              <div class="dict-card-title">
                <span class="dict-headword">
                  {{ filteredResults[vRow.index]!.nikud ?? filteredResults[vRow.index]!.headword }}
                </span>
                <span v-if="typeLabel(filteredResults[vRow.index]!)" class="dict-type-badge">
                  {{ typeLabel(filteredResults[vRow.index]!) }}
                </span>
                <span class="dict-source-badge">
                  {{ filteredResults[vRow.index]!.bookTitle }}
                </span>
              </div>
              <button
                v-if="filteredResults[vRow.index]!.bookId !== null"
                class="dict-open-btn"
                @click="emit('openInViewer', filteredResults[vRow.index]!.id)"
              >
                <IconOpen20Regular />
              </button>
            </div>

            <!-- Inline content for non-book entries (always visible) -->
            <div v-if="hasInlineContent(filteredResults[vRow.index]!)" class="dict-card-body">
              <div
                v-if="expandedEntries.get(filteredResults[vRow.index]!.id) === null"
                class="dict-entry-loading"
              >
                טוען...
              </div>
              <DictionaryEntryView
                v-else-if="expandedEntries.get(filteredResults[vRow.index]!.id)"
                :entry="expandedEntries.get(filteredResults[vRow.index]!.id)!"
              />
            </div>

            <!-- Book entries: preview + expand toggle -->
            <template v-else>
              <div class="dict-card-preview">
                {{ bookPreview(filteredResults[vRow.index]!) }}
              </div>
              <button class="dict-expand-btn" @click="emit('toggle', filteredResults[vRow.index]!)">
                {{ expandedEntries.has(filteredResults[vRow.index]!.id) ? 'הסתר' : 'הצג הכל' }}
              </button>
              <div
                v-if="expandedEntries.has(filteredResults[vRow.index]!.id)"
                class="dict-card-expanded"
              >
                <div
                  v-if="expandedEntries.get(filteredResults[vRow.index]!.id) === null"
                  class="dict-entry-loading"
                >
                  טוען...
                </div>
                <DictionaryEntryView
                  v-else-if="expandedEntries.get(filteredResults[vRow.index]!.id)"
                  :entry="expandedEntries.get(filteredResults[vRow.index]!.id)!"
                />
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.results-wrap {
  display: contents;
}

/* ── Filter bar ── */
.dict-filter-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: space-between;
  direction: rtl;
  gap: 3px 0;
  padding: 3px 8px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.dict-filter-bar::after {
  content: '';
  flex: 1 0 0;
}

.dict-filter-tab {
  display: flex;
  align-items: center;
  gap: 3px;
  height: 22px;
  padding: 0 7px;
  border-radius: 999px;
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  border: 1px solid transparent;
}

.dict-filter-tab.active {
  background: color-mix(in srgb, var(--accent-color) 15%, transparent);
  border-color: color-mix(in srgb, var(--accent-color) 40%, transparent);
  color: var(--accent-color);
}

.dict-filter-count {
  font-size: 10px;
  opacity: 0.65;
}

/* ── Scroll container ── */
.dict-scroll {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
  outline: none;
}

.dict-state-msg {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px 16px;
  font-size: 13px;
  color: var(--text-secondary);
}

/* ── Card ── */
.dict-card {
  border-bottom: 1px solid var(--border-color);
  direction: rtl;
  padding: 6px 0 0;
}

/* ── Card header ── */
.dict-card-header {
  display: flex;
  align-items: flex-start;
  gap: 4px;
  padding: 0 10px 4px;
}

.dict-card-title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 5px;
  flex: 1;
  min-width: 0;
}

.dict-headword {
  font-size: 14px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
}

.dict-type-badge {
  font-size: 10px;
  font-weight: 600;
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 12%, transparent);
  border: 1px solid color-mix(in srgb, var(--accent-color) 30%, transparent);
  border-radius: 999px;
  padding: 0 5px;
  line-height: 16px;
  flex-shrink: 0;
}

.dict-source-badge {
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 10%, transparent);
  border-radius: 999px;
  padding: 0 5px;
  line-height: 16px;
  flex-shrink: 0;
  white-space: nowrap;
}

.dict-open-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  flex-shrink: 0;
  color: var(--text-secondary);
  border-radius: 4px;
}

.dict-open-btn:hover {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 10%, transparent);
}

/* ── Card body (inline content) ── */
.dict-card-body {
  padding: 0 10px 8px;
}

/* ── Book preview ── */
.dict-card-preview {
  padding: 0 10px 4px;
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.dict-expand-btn {
  display: inline-flex;
  align-items: center;
  height: 20px;
  padding: 0 10px;
  margin-bottom: 6px;
  font-size: 11px;
  color: var(--accent-color);
  background: none;
  border: none;
  cursor: pointer;
  border-radius: 0;
}

.dict-expand-btn:hover {
  text-decoration: underline;
  background: none;
}

.dict-card-expanded {
  border-top: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  background: var(--bg-secondary);
}

.dict-entry-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 10px 16px;
  font-size: 12px;
  color: var(--text-secondary);
}
</style>
