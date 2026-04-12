<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import WiktionaryEntry from './WiktionaryEntry.vue'
import { useWiktionary } from './useWiktionary'
import { useTabStore } from '@/stores/tabStore'
import { useListKeys } from '@/composables/useListKeyNav'

const tabStore = useTabStore()

const {
  searchQuery,
  debouncedQuery,
  result,
  suggestions,
  searching,
  hasSearched,
  notFound,
  error,
  search,
  searchWord,
  loadSuggestions,
  clearSuggestions,
} = useWiktionary()

watch(debouncedQuery, (q) => {
  search(q)
  loadSuggestions(q)
})

watch(result, (r) => {
  tabStore.updateActiveTab({ title: r ? `מילון · ${r.title}` : 'מילון' })
})

// ── Keyboard nav for suggestions ──────────────────────────────────────────────

const suggestionsEl = ref<HTMLElement | null>(null)
const inputEl = ref<HTMLInputElement | null>(null)

const { focusedIndex } = useListKeys(
  suggestionsEl,
  () => suggestions.value.length,
  (i) => {
    const s = suggestions.value[i]
    if (s) pickSuggestion(s)
  },
  { itemSelector: '.dict-suggestion-item' },
)

function pickSuggestion(word: string) {
  searchWord(word)
  inputEl.value?.focus()
}

function onInputKeydown(e: KeyboardEvent) {
  if (!suggestions.value.length) return
  if (e.code === 'ArrowDown' || e.code === 'ArrowUp' || e.code === 'Tab') {
    e.preventDefault()
    suggestionsEl.value?.focus()
  }
  if (e.code === 'Escape') clearSuggestions()
}
</script>

<template>
  <div class="dict-page">
    <!-- Results area -->
    <div class="dict-scroll">
      <div v-if="searching" class="dict-state">מחפש...</div>
      <div v-else-if="error" class="dict-state dict-error">{{ error }}</div>
      <div v-else-if="hasSearched && notFound" class="dict-state">לא נמצא</div>

      <!-- Suggestions while typing -->
      <div
        v-else-if="suggestions.length > 0"
        ref="suggestionsEl"
        class="dict-suggestions"
        tabindex="0"
      >
        <button
          v-for="(s, i) in suggestions"
          :key="s"
          class="dict-suggestion-item"
          :class="{ focused: focusedIndex === i }"
          @click="pickSuggestion(s)"
        >
          {{ s }}
        </button>
      </div>

      <div v-else-if="result" class="dict-document">
        <WiktionaryEntry
          v-for="(sense, i) in result.senses"
          :key="i"
          :sense="sense"
          :index="i"
          :total="result.senses.length"
          @search-word="searchWord"
        />
        <div class="dict-attribution">
          מקור: <a href="https://he.wiktionary.org" target="_blank" rel="noopener">ויקימילון</a>
          · רישיון CC BY-SA 4.0
        </div>
      </div>

      <div v-else class="dict-empty">
        <IconSearch20Regular class="dict-empty-icon" />
      </div>
    </div>

    <!-- Search bar -->
    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        ref="inputEl"
        v-model="searchQuery"
        class="dict-search-input"
        type="search"
        placeholder="חפש מילה"
        dir="rtl"
        autofocus
        @keydown="onInputKeydown"
        @keydown.enter="clearSuggestions"
      />
    </BottomSearchBar>
  </div>
</template>

<style scoped>
.dict-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: var(--bg-primary);
  direction: rtl;
}

.dict-scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  user-select: text;
}

/* ── States ── */
.dict-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  font-size: 13px;
  color: var(--text-secondary);
  padding: 40px 20px;
}
.dict-error {
  color: color-mix(in srgb, red 60%, var(--text-secondary));
}

/* ── Empty ── */
.dict-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-secondary);
  opacity: 0.5;
}
.dict-empty-icon {
  width: 48px;
  height: 48px;
}

/* ── Document ── */
.dict-document {
  padding: 14px 16px 32px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* ── Attribution ── */
.dict-attribution {
  font-size: 10px;
  color: var(--text-secondary);
  opacity: 0.6;
  padding-top: 8px;
  border-top: 1px solid var(--border-color);
}
.dict-attribution a {
  color: var(--accent-color);
  text-decoration: none;
}

/* ── Suggestions ── */
.dict-suggestions {
  display: flex;
  flex-direction: column;
}
.dict-suggestion-item {
  height: 44px;
  padding: 0 14px;
  text-align: start;
  font-size: 13px;
  color: var(--text-primary);
  background: none;
  border: none;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  cursor: pointer;
  direction: rtl;
  flex-shrink: 0;
}
.dict-suggestion-item:hover,
.dict-suggestion-item.focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

/* ── Search bar ── */
.search-icon {
  color: var(--text-secondary);
}
.dict-search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  font-family: inherit;
  direction: rtl;
}
.dict-search-input::placeholder {
  color: var(--text-secondary);
}
.dict-search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
</style>
