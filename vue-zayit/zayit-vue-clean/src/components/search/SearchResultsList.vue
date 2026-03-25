<script setup lang="ts">
import { ref, computed } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { BloomSearchResult } from './searchTypes'

const props = defineProps<{
  results: BloomSearchResult[]
  searchQuery: string
  isSearching: boolean
  hasSearched: boolean
}>()

const emit = defineEmits<{ resultClick: [BloomSearchResult] }>()

const settingsStore = useSettingsStore()
const scrollEl = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(computed(() => ({
  count: props.results.length,
  getScrollElement: () => scrollEl.value,
  estimateSize: () => 80,
  overscan: 8,
})))

useVirtualScrollerKeys(
  scrollEl,
  () => virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.results.length,
)

function highlight(snippet: string): string {
  if (!props.searchQuery || !snippet) return snippet
  let text = settingsStore.censorDivineNames ? censorDivineNames(snippet) : snippet
  const terms = props.searchQuery.trim().split(/\s+/).filter(Boolean)
  for (const term of terms) {
    const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    text = text.replace(new RegExp(`(${escaped})`, 'gi'), '<span class="match">$1</span>')
  }
  return text
}
</script>

<template>
  <div class="results-wrap">
    <!-- Empty / no-search state -->
    <div v-if="!hasSearched || (results.length === 0 && !isSearching)" class="empty-state">
      <IconSearchSparkle24 class="empty-icon" />
      <span v-if="hasSearched && results.length === 0" class="empty-msg">לא נמצאו תוצאות</span>
    </div>

    <!-- Virtualised list -->
    <div v-else ref="scrollEl" class="scroller" tabindex="0">
      <div :style="{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }">
        <div
          v-for="vRow in virtualizer.getVirtualItems()"
          :key="String(vRow.key)"
          :style="{ position: 'absolute', top: 0, left: 0, right: 0, transform: `translateY(${vRow.start}px)` }"
        >
          <div class="result-item" @click="emit('resultClick', results[vRow.index]!)">
            <div class="result-header">
              <span class="book-title">{{ results[vRow.index]!.bookTitle }}</span>
              <span v-if="results[vRow.index]!.tocText" class="sep">›</span>
              <span v-if="results[vRow.index]!.tocText" class="toc-text">{{ results[vRow.index]!.tocText }}</span>
            </div>
            <!-- eslint-disable-next-line vue/no-v-html -->
            <div class="snippet" v-html="highlight(results[vRow.index]!.snippet)" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.results-wrap {
  flex: 1;
  overflow: hidden;
  position: relative;
}

.empty-state {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}

.empty-icon {
  width: 56px;
  height: 56px;
  opacity: 0.25;
}

.empty-msg {
  font-size: 14px;
  color: var(--text-secondary);
}

.scroller {
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  outline: none;
}

.result-item {
  padding: 8px 14px;
  border-bottom: 1px solid var(--border-color);
  cursor: pointer;
  transition: background 0.1s;
}

.result-item:hover { background: var(--hover-bg); }
.result-item:active { background: var(--active-bg); }

.result-header {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-bottom: 4px;
  font-family: var(--header-font);
  font-weight: 500;
  font-size: 13px;
}

.book-title { color: var(--accent-color); }
.sep        { color: var(--text-secondary); font-size: 11px; }
.toc-text   { color: var(--text-secondary); font-size: 12px; }

.snippet {
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  direction: rtl;
  text-align: justify;
  user-select: text;
}

.snippet :deep(.match) {
  color: var(--accent-color);
  font-weight: 600;
}
</style>
