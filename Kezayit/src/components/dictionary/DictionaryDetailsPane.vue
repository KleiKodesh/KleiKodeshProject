<script setup lang="ts">
import { computed } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import WiktionaryEntry from './WiktionaryEntry.vue'
import type { WiktionarySense } from './useWiktionary'
import type { DictSuggestion } from './useAramaicSearch'

const props = defineProps<{
  searching: boolean
  error: string | null
  notFound: boolean
  allSenses: WiktionarySense[]
  wikiSensesCount: number
  aramaicSensesCount: number
  suggestions: DictSuggestion[]
  queryLength: number
  hasWikiAttribution: boolean
}>()

const emit = defineEmits<{
  pick: [word: string]
  searchWord: [word: string]
}>()

const detailChips = computed(() => {
  const seen = new Set<string>()
  const filtered = props.suggestions
    .filter((s) => s.headword.length <= props.queryLength + 2)
    .filter((s) => {
      if (seen.has(s.headword)) return false
      seen.add(s.headword)
      return true
    })
  // If the length filter leaves nothing, show all suggestions (deduplicated)
  if (filtered.length > 0) return filtered
  const seen2 = new Set<string>()
  return props.suggestions.filter((s) => {
    if (seen2.has(s.headword)) return false
    seen2.add(s.headword)
    return true
  })
})

function isFirstAramaic(index: number): boolean {
  return (
    index === props.wikiSensesCount && props.aramaicSensesCount > 0 && props.wikiSensesCount > 0
  )
}
</script>

<template>
  <div class="details-pane">
    <div v-if="searching" class="dict-state">מחפש...</div>
    <div v-else-if="error" class="dict-state dict-error">{{ error }}</div>
    <template v-else>
      <!-- Suggestion chips — always shown when available, regardless of results -->
      <div v-if="detailChips.length > 1" class="dict-detail-suggestions">
        <span class="dict-detail-suggestions-label">הצעות</span>
        <span
          v-for="(s, i) in detailChips"
          :key="`dc-${s.headword}-${i}`"
          class="dict-detail-sugg"
          @click="emit('pick', s.headword)"
          >{{ s.headword }}</span
        >
      </div>

      <div v-if="notFound" class="dict-state">לא נמצא</div>

      <div v-else-if="allSenses.length > 0" class="dict-document">
        <!-- Senses -->
        <template v-for="(sense, i) in allSenses" :key="`${sense.headword}-${i}`">
          <div v-if="isFirstAramaic(i)" class="dict-section-divider">
            <span>מקורות נוספים</span>
          </div>
          <WiktionaryEntry
            :sense="sense"
            :index="i"
            :total="allSenses.length"
            @search-word="emit('searchWord', $event)"
          />
        </template>

        <div v-if="hasWikiAttribution" class="dict-attribution">
          מקור: <a href="https://he.wiktionary.org" target="_blank" rel="noopener">ויקימילון</a>
          · רישיון CC BY-SA 4.0
        </div>
      </div>

      <div v-else class="dict-empty">
        <IconSearch20Regular class="dict-empty-icon" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.details-pane {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  user-select: text;
}

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

.dict-document {
  padding: 14px 16px 32px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* ── Detail suggestion chips ── */
.dict-detail-suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  padding: 8px 14px;
  border-bottom: 1px solid var(--border-color);
}
.dict-detail-suggestions-label {
  width: 100%;
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.03em;
  margin-bottom: 2px;
}
.dict-detail-sugg {
  font-size: 13px;
  font-weight: 600;
  color: var(--accent-color);
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
  line-height: 1.6;
}
.dict-detail-sugg:hover {
  opacity: 0.8;
}

/* ── Section divider ── */
.dict-section-divider {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.04em;
}
.dict-section-divider::before,
.dict-section-divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border-color);
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
</style>
