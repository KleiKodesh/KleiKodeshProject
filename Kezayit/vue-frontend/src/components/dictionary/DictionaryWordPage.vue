<script setup lang="ts">
import { computed } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { WordPageData } from './DictionaryPage.vue'

const props = defineProps<{ data: WordPageData }>()
const emit  = defineEmits<{ (e: 'select', headword: string): void }>()

const settings = useSettingsStore()

function maybeFilter(text: string): string {
  return settings.censorDivineNames ? censorDivineNames(text) : text
}

// ── Definitions ───────────────────────────────────────────────────────────────
// Group senses by nikud heading — if a word has nikud, use it as the group heading,
// otherwise use the plain headword.

interface SenseGroup {
  heading: string
  items:   { text: string; sourceLabel: string | null }[]
}

const SOURCE_ORDER: Record<number, number> = { 5: 0, 1: 1, 7: 2, 6: 3, 2: 4, 4: 5, 3: 6 }

const senseGroups = computed((): SenseGroup[] => {
  const sorted = [...props.data.senses].sort(
    (a, b) => (SOURCE_ORDER[a.source_id ?? 99] ?? 99) - (SOURCE_ORDER[b.source_id ?? 99] ?? 99)
  )
  const map = new Map<string, SenseGroup>()
  for (const r of sorted) {
    const heading = r.nikud ?? r.headword
    if (!map.has(heading)) map.set(heading, { heading, items: [] })
    map.get(heading)!.items.push({ text: r.text, sourceLabel: r.source ?? null })
  }
  return [...map.values()]
})

// ── Related words ─────────────────────────────────────────────────────────────

const allLinks = computed(() => {
  const seen = new Set<string>()
  const words: string[] = []
  for (const l of props.data.links) {
    if (!seen.has(l.word)) { seen.add(l.word); words.push(l.word) }
  }
  return words
})

const allRelated = computed(() => [
  ...props.data.synonyms,
  ...allLinks.value,
  ...props.data.variants,
])
</script>

<template>
  <article class="word-page" dir="rtl">

    <h1 class="word-title">{{ data.headword }}</h1>

    <!-- Definitions -->
    <section v-if="senseGroups.length" class="senses-section">
      <div v-for="(g, gi) in senseGroups" :key="gi" class="sense-group">
        <span v-if="senseGroups.length > 1" class="sense-heading">{{ g.heading }}</span>
        <ol class="sense-list">
          <li v-for="(item, i) in g.items" :key="i" class="sense-item">
            {{ maybeFilter(item.text) }}<span v-if="item.sourceLabel" class="sense-source"> ({{ item.sourceLabel }})</span>
          </li>
        </ol>
      </div>
    </section>

    <!-- Related -->
    <section v-if="allRelated.length" class="related-section">
      <h2 class="section-title">קשורים</h2>
      <p class="related-line">
        <span v-for="(w, i) in allRelated" :key="w">
          <button class="word-link" @click="emit('select', w)">{{ w }}</button><span v-if="i < allRelated.length - 1" class="comma">, </span>
        </span>
      </p>
    </section>

  </article>
</template>

<style scoped>
.word-page {
  padding: 12px 16px;
  direction: rtl;
  display: flex;
  flex-direction: column;
  gap: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--text-primary);
  height: 100%;
  overflow: hidden;
  user-select: text;
  -webkit-user-select: text;
}

.word-title {
  font-size: 22px;
  font-weight: 700;
  margin: 0 0 8px;
  line-height: 1.2;
  flex-shrink: 0;
}

.section-title {
  font-size: 10px;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
  margin: 0 0 4px;
  text-transform: uppercase;
  flex-shrink: 0;
}

.senses-section {
  flex: 1 1 0;
  min-height: 60px;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  padding-bottom: 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}

.sense-group { margin-bottom: 6px; }

.sense-heading {
  display: block;
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 2px;
}

.sense-list {
  margin: 0;
  padding-inline-start: 18px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.sense-item { font-size: 13px; line-height: 1.6; }

.sense-source {
  font-size: 11px;
  color: var(--text-secondary);
}

.related-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  max-height: 25%;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  flex-shrink: 0;
}

.related-line {
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
}

.comma { color: var(--text-secondary); }

.word-link {
  font-size: 12px;
  font-family: inherit;
  color: var(--accent-color);
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  text-decoration: none;
  user-select: text;
  -webkit-user-select: text;
}
.word-link:hover {
  color: color-mix(in srgb, var(--accent-color) 80%, var(--text-primary));
}
</style>
