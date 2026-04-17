<script setup lang="ts">
import { watch, ref } from 'vue'
import { useDebounce } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import { useKezayitDictionary } from './useKezayitDictionary'
import DictionaryRow from './DictionaryRow.vue'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const searchQuery = ref('')
const debouncedQuery = useDebounce(searchQuery, 300)
const { senses, searching, search } = useKezayitDictionary()

watch(debouncedQuery, (q) => search(q))

watch(senses, (s) => {
  const first = s[0]
  if (first) tabStore.updateActiveTab({ title: `מילון · ${first.headword}` })
  else tabStore.updateActiveTab({ title: 'מילון' })
})
</script>

<template>
  <div class="dict-page">
    <div class="dict-body">
      <div v-if="searching" class="dict-state">מחפש...</div>

      <div v-else-if="senses.length === 0 && debouncedQuery.length > 0" class="dict-state">
        לא נמצאו תוצאות
      </div>

      <div v-else-if="senses.length > 0" class="dict-list">
        <div v-if="senses[0]?.isFuzzy" class="dict-fuzzy-label">הצעות דומות</div>
        <DictionaryRow v-for="(sense, i) in senses" :key="i" :sense="sense" />
      </div>

      <div v-else class="dict-empty">
        <IconSearch20Regular class="dict-empty-icon" />
      </div>
    </div>

    <BottomSearchBar>
      <template #left>
        <IconSearch20Regular class="search-icon" />
      </template>
      <input
        v-model="searchQuery"
        class="dict-search-input"
        type="search"
        placeholder="חפש מילה"
        dir="rtl"
        autofocus
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

.dict-body {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── States ── */
.dict-state {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  font-size: 13px;
  color: var(--text-secondary);
}

.dict-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-secondary);
  opacity: 0.4;
}
.dict-empty-icon {
  width: 48px;
  height: 48px;
}

/* ── Fuzzy label ── */
.dict-fuzzy-label {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.04em;
  padding: 6px 14px 2px;
}

/* ── List ── */
.dict-list {
  display: flex;
  flex-direction: column;
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
