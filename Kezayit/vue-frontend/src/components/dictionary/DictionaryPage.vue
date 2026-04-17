<script setup lang="ts">
import { watch } from 'vue'
import { useDebounce } from '@vueuse/core'
import { ref } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'
import { useKezayitDictionary } from './useKezayitDictionary'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import { useTabStore } from '@/stores/tabStore'

const tabStore = useTabStore()
const settings = useSettingsStore()

const searchQuery = ref('')
const debouncedQuery = useDebounce(searchQuery, 300)

const { senses, searching, thesaurusGroups, search } = useKezayitDictionary()

watch(debouncedQuery, (q) => search(q))

watch(senses, (s) => {
  const first = s[0]
  if (first) tabStore.updateActiveTab({ title: `מילון · ${first.headword}` })
  else tabStore.updateActiveTab({ title: 'מילון' })
})

function maybeFilter(text: string): string {
  return settings.censorDivineNames ? censorDivineNames(text) : text
}
</script>

<template>
  <div class="dict-page">
    <div class="dict-body">
      <div v-if="searching" class="dict-state">מחפש...</div>

      <div v-else-if="senses.length === 0 && debouncedQuery.length > 0" class="dict-state">
        לא נמצאו תוצאות
      </div>

      <template v-if="thesaurusGroups.length > 0">
        <div class="dict-section-label">מילים נרדפות</div>
        <div class="dict-thesaurus">
          <div v-for="(group, gi) in thesaurusGroups" :key="gi" class="dict-thesaurus-group">
            <span v-for="(word, wi) in group" :key="wi" class="dict-thesaurus-word">{{ word }}</span>
          </div>
        </div>
      </template>      <div v-else-if="senses.length > 0" class="dict-list">
        <div v-if="senses[0]?.isFuzzy" class="dict-fuzzy-label">הצעות דומות</div>
        <div v-for="(sense, i) in senses" :key="i" class="dict-row">
          <span class="dict-headword">{{ sense.headword }}</span>
          <span class="dict-sep">—</span>
          <span class="dict-definition">{{ maybeFilter(sense.definition) }}</span>
          <span v-if="sense.sourceLabel" class="dict-source">{{ sense.sourceLabel }}</span>
        </div>
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

.dict-row {
  display: flex;
  align-items: baseline;
  gap: 6px;
  min-height: 44px;
  padding: 8px 14px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  flex-wrap: wrap;
}

.dict-headword {
  font-size: 13px;
  font-weight: 700;
  color: var(--text-primary);
  flex-shrink: 0;
}

.dict-sep {
  font-size: 13px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.dict-definition {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
  flex: 1;
  min-width: 0;
}

.dict-source {
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 10%, transparent);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 16px;
  flex-shrink: 0;
  align-self: center;
}

/* ── Section label ── */
.dict-section-label {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.04em;
  padding: 10px 14px 4px;
  border-top: 1px solid var(--border-color);
}

/* ── Thesaurus ── */
.dict-thesaurus {
  padding: 0 14px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.dict-thesaurus-group {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
}
.dict-thesaurus-word {
  font-size: 13px;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
  border-radius: 4px;
  padding: 1px 7px;
  line-height: 22px;
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
