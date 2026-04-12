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
    estimateSize: () => 36,
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

// Re-measure all rows when expanded content loads in (heights change)
watch(
  () => props.expandedEntries,
  () => virtualizer.value.measure(),
  { deep: true },
)
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

    <!-- List -->
    <div v-if="searching" class="dict-state-msg">מחפש...</div>
    <div v-else-if="hasSearched && filteredResults.length === 0" class="dict-state-msg">
      לא נמצאו תוצאות
    </div>
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
          <div class="dict-result-item">
            <div class="dict-result-row">
              <button
                class="dict-result-toggle"
                @click="emit('toggle', filteredResults[vRow.index]!)"
              >
                <span class="dict-result-headword">
                  {{ filteredResults[vRow.index]!.nikud ?? filteredResults[vRow.index]!.headword }}
                </span>
                <span class="dict-result-source">
                  {{ filteredResults[vRow.index]!.bookTitle }}
                </span>
              </button>
              <button
                v-if="filteredResults[vRow.index]!.bookId !== null"
                class="dict-open-btn"
                @click="emit('openInViewer', filteredResults[vRow.index]!.id)"
              >
                <IconOpen20Regular />
              </button>
            </div>

            <div
              v-if="expandedEntries.has(filteredResults[vRow.index]!.id)"
              class="dict-result-body"
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

.dict-result-item {
  border-bottom: 1px solid var(--border-color);
}

.dict-result-row {
  display: flex;
  align-items: center;
  direction: rtl;
  width: 100%;
  height: 36px;
}

.dict-result-toggle {
  display: flex;
  align-items: center;
  flex: 1;
  height: 36px;
  padding: 0 10px;
  gap: 6px;
  background: none;
  border: none;
  border-radius: 0;
  cursor: pointer;
  text-align: start;
  direction: rtl;
}

.dict-result-toggle:hover {
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}

.dict-result-toggle:active {
  background: color-mix(in srgb, var(--text-primary) 9%, transparent);
}

.dict-result-headword {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  flex: 1;
  line-height: 1;
}

.dict-result-source {
  font-size: 10px;
  color: var(--text-secondary);
  flex-shrink: 0;
  white-space: nowrap;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  padding: 1px 5px;
  border-radius: 999px;
  line-height: 1;
}

.dict-open-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  color: var(--text-secondary);
  border-inline-start: 1px solid var(--border-color);
}

.dict-open-btn:hover {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
}

.dict-result-body {
  border-top: 1px solid var(--border-color);
  background: var(--bg-secondary);
}

.dict-entry-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 12px 16px;
  font-size: 12px;
  color: var(--text-secondary);
}
</style>
