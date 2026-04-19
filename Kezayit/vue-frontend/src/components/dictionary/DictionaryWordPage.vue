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

// ── Grammar entries ───────────────────────────────────────────────────────────
// Each unique wiki sense becomes one grammar entry: nikud + its own grammar facts.
// Multiple senses with the same nikud are merged (deduped grammar facts per nikud).

interface GrammarEntry {
  nikud:   string
  pos:     string[]
  shoresh: string[]
  binyan:  string[]
}

const grammarEntries = computed((): GrammarEntry[] => {
  const map = new Map<string, GrammarEntry>()

  for (const s of props.data.wikiSenses) {
    const key = s.nikud ?? ''
    if (!key) continue
    if (!map.has(key)) map.set(key, { nikud: key, pos: [], shoresh: [], binyan: [] })
    const e = map.get(key)!
    if (s.pos    && !e.pos.includes(s.pos))       e.pos.push(s.pos)
    if (s.shoresh && !e.shoresh.includes(s.shoresh)) e.shoresh.push(s.shoresh)
    if (s.binyan  && !e.binyan.includes(s.binyan))   e.binyan.push(s.binyan)
  }

  // Add kezayit-only nikud forms not in wikidict
  const wikiNikud = new Set(props.data.wikiSenses.map(s => s.nikud).filter(Boolean))
  for (const n of props.data.nikud) {
    if (!wikiNikud.has(n)) map.set(n, { nikud: n, pos: [], shoresh: [], binyan: [] })
  }

  return [...map.values()]
})

// Build a short grammar description string for a single entry
function grammarDesc(e: GrammarEntry): string {
  const parts: string[] = []
  if (e.pos.length)     parts.push(e.pos.join(' / '))
  if (e.binyan.length)  parts.push(e.binyan.join(' / '))
  if (e.shoresh.length) parts.push(`שורש ${e.shoresh.join(' / ')}`)
  return parts.join(' · ')
}

// ── Definitions grouped by nikud ─────────────────────────────────────────────

const SOURCE_ORDER: Record<number, number> = { 5: 0, 1: 1, 6: 2, 2: 3, 4: 4, 3: 5 }

interface DefGroup {
  heading: string
  defs:    { text: string; sourceLabel: string | null }[]
}

const defGroups = computed((): DefGroup[] => {
  const sorted = [...props.data.kezayitRows].sort(
    (a, b) => (SOURCE_ORDER[a.source_id ?? 99] ?? 99) - (SOURCE_ORDER[b.source_id ?? 99] ?? 99)
  )

  const map = new Map<string, DefGroup>()
  for (const r of sorted) {
    const heading = r.nikud ?? r.headword
    if (!map.has(heading)) map.set(heading, { heading, defs: [] })
    map.get(heading)!.defs.push({ text: r.definition, sourceLabel: r.source ?? null })
  }

  return [...map.values()]
})

// ── Related words ─────────────────────────────────────────────────────────────

const allRelated = computed(() => {
  const seen = new Set<string>()
  const words: string[] = []
  for (const r of props.data.related) {
    if (!seen.has(r.word)) { seen.add(r.word); words.push(r.word) }
  }
  return words
})

// ktiv_male forms from sense rows, deduped
const ktivMaleForms = computed(() => {
  const seen = new Set([
    props.data.headword,
    ...props.data.variants,
    ...props.data.synonyms,
  ])
  const forms: string[] = []
  for (const s of props.data.wikiSenses) {
    if (s.ktiv_male && !seen.has(s.ktiv_male)) {
      seen.add(s.ktiv_male)
      forms.push(s.ktiv_male)
    }
  }
  return forms
})
</script>

<template>
  <article class="word-page" dir="rtl">

    <h1 class="word-title">{{ data.headword }}</h1>

    <!-- Grammar entries: one line per nikud form -->
    <section v-if="grammarEntries.length" class="grammar-section">
      <h2 class="section-title">צורות וניקוד</h2>
      <div v-for="e in grammarEntries" :key="e.nikud" class="grammar-entry">
        <span class="g-nikud">{{ e.nikud }}</span>
        <span v-if="grammarDesc(e)" class="g-desc">{{ grammarDesc(e) }}</span>
      </div>
    </section>

    <!-- Definitions -->
    <section v-if="defGroups.length" class="defs-section">
      <h2 class="section-title">משמעויות</h2>
      <div v-for="(g, gi) in defGroups" :key="gi" class="def-group">
        <span v-if="defGroups.length > 1" class="def-group-heading">{{ g.heading }}</span>
        <ol class="def-list">
          <li v-for="(d, i) in g.defs" :key="i" class="def-item">
            {{ maybeFilter(d.text) }}<span v-if="d.sourceLabel" class="def-source"> ({{ d.sourceLabel }})</span>
          </li>
        </ol>
      </div>
    </section>

    <!-- קשורים: synonyms + related + variants + ktiv male -->
    <section v-if="data.synonyms.length || allRelated.length || data.variants.length || ktivMaleForms.length" class="related-section">
      <h2 class="section-title">קשורים</h2>
      <p class="related-line">
        <span v-for="(w, i) in [...data.synonyms, ...allRelated, ...data.variants, ...ktivMaleForms]" :key="w">
          <button class="word-link word-link--plain" @click="emit('select', w)">{{ w }}</button><span v-if="i < data.synonyms.length + allRelated.length + data.variants.length + ktivMaleForms.length - 1" class="comma">, </span>
        </span>
      </p>
    </section>

  </article>
</template>

<style scoped>
.word-page {
  padding: 12px 16px 12px;
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

/* ── Title ── */
.word-title {
  font-size: 22px;
  font-weight: 700;
  color: var(--text-primary);
  margin: 0 0 8px;
  line-height: 1.2;
  flex-shrink: 0;
}

/* ── Section title ── */
.section-title {
  font-size: 10px;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
  margin: 0 0 4px;
  text-transform: uppercase;
  flex-shrink: 0;
}

/* ── Grammar ── */
.grammar-section {
  display: flex;
  flex-direction: column;
  gap: 1px;
  padding-bottom: 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  max-height: 30%;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  flex-shrink: 0;
}

/* ── Definitions ── */
.defs-section {
  padding-bottom: 8px;
  padding-top: 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  flex: 1 1 0;
  min-height: 80px;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.def-group {
  margin-bottom: 6px;
}

.def-group-heading {
  display: block;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 2px;
}

/* ── Related sections ── */
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

.grammar-entry {
  display: flex;
  align-items: baseline;
  gap: 8px;
  line-height: 1.7;
}

.g-nikud {
  font-size: 15px;
  font-weight: 700;
  color: var(--text-primary);
  flex-shrink: 0;
}

.g-desc {
  font-size: 11px;
  color: var(--text-secondary);
  font-style: italic;
}

/* ── Definitions ── */
.def-list {
  margin: 0;
  padding-inline-start: 18px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.def-item {
  font-size: 13px;
  line-height: 1.6;
}

.def-source {
  font-size: 11px;
  color: var(--text-secondary);
}

.related-line {
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-primary);
}

.comma {
  color: var(--text-secondary);
}

.word-link {
  font-size: 12px;
  font-family: inherit;
  color: var(--accent-color);
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
  text-decoration-color: color-mix(in srgb, var(--accent-color) 40%, transparent);
  user-select: text;
  -webkit-user-select: text;
}

.word-link:hover {
  text-decoration-color: var(--accent-color);
}

.word-link--plain {
  text-decoration: none;
}
.word-link--plain:hover {
  color: color-mix(in srgb, var(--accent-color) 80%, var(--text-primary));
}
</style>
