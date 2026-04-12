<script setup lang="ts">
import { computed } from 'vue'
import { IconOpen20Regular } from '@iconify-prerendered/vue-fluent'
import type { DictEntry, DictEntryContent } from './useDictionarySearch'

const props = defineProps<{
  results: DictEntry[]
  filteredResults: DictEntry[]
  searching: boolean
  hasSearched: boolean
  expandedEntries: Map<number, DictEntryContent | null>
  lastTerm: string
}>()

const emit = defineEmits<{
  openInViewer: [entryId: number]
  searchWord: [word: string]
}>()

// ── Split exact vs inflections ────────────────────────────────────────────────

function levenshtein(a: string, b: string): number {
  const m = a.length,
    n = b.length
  const dp: number[] = Array.from({ length: n + 1 }, (_, i) => i)
  for (let i = 1; i <= m; i++) {
    let prev = dp[0]!
    dp[0] = i
    for (let j = 1; j <= n; j++) {
      const temp = dp[j]!
      dp[j] = a[i - 1] === b[j - 1] ? prev : 1 + Math.min(prev, dp[j]!, dp[j - 1]!)
      prev = temp
    }
  }
  return dp[n]!
}

interface EntryGroup {
  headword: string
  nikud: string | null
  nikudVariants: string[]
  entries: DictEntry[]
  crossRefs: string[]
}

function extractAllNikud(e: DictEntry): string[] {
  const found: string[] = []
  if (e.nikud) found.push(e.nikud)
  const raw = e.definition ?? ''
  if (!raw.includes('<')) {
    for (const part of raw.split('***')) {
      const m = part.trim().match(/^\{([^}]+)\}/)
      if (m && m[1] && !found.includes(m[1])) found.push(m[1])
    }
  }
  return found
}

function buildGroups(entries: DictEntry[]): EntryGroup[] {
  const map = new Map<string, EntryGroup>()
  for (const e of entries) {
    if (!map.has(e.headword)) {
      map.set(e.headword, {
        headword: e.headword,
        nikud: e.nikud,
        nikudVariants: [],
        entries: [],
        crossRefs: [],
      })
    }
    const g = map.get(e.headword)!
    const ref = crossRef(e)
    if (ref) {
      if (!g.crossRefs.includes(ref)) g.crossRefs.push(ref)
    } else {
      g.entries.push(e)
    }
    // Collect all unique nikud forms including per-sense variants
    for (const n of extractAllNikud(e)) {
      if (!g.nikudVariants.includes(n)) g.nikudVariants.push(n)
    }
    // Best nikud: prefer source 3
    if (e.nikud && e.source === 3) g.nikud = e.nikud
    else if (e.nikud && !g.nikud) g.nikud = e.nikud
  }
  return Array.from(map.values())
}
const exactGroups = computed(() =>
  buildGroups(props.filteredResults.filter((e) => e.matchTier === 0)),
)

/** Unique headwords for non-exact results, sorted by levenshtein distance to search term */
const inflectionWords = computed<{ headword: string; nikud: string | null }[]>(() => {
  const seen = new Map<string, { headword: string; nikud: string | null }>()
  for (const e of props.filteredResults) {
    if (e.matchTier !== 0 && !seen.has(e.headword)) {
      seen.set(e.headword, { headword: e.headword, nikud: e.nikud })
    }
  }
  return [...seen.values()].sort(
    (a, b) =>
      levenshtein(a.nikud ?? a.headword, props.lastTerm) -
      levenshtein(b.nikud ?? b.headword, props.lastTerm),
  )
})

// ── Entry rendering helpers ───────────────────────────────────────────────────

interface Sense {
  nikud: string | null
  text: string
  expansion: string | null
}

function parseSenses(entry: DictEntry): Sense[] {
  const raw = entry.definition ?? ''
  if (raw.startsWith('REDIRECT') || raw.startsWith('הפניה')) return []
  if (raw.includes('<') && raw.includes('>')) return [{ nikud: null, text: raw, expansion: null }]

  return raw
    .split('***')
    .map((s) => s.trim())
    .filter(Boolean)
    .map((part) => {
      const nikudMatch = part.match(/^\{([^}]+)\}\s*(.*)$/)
      const text = nikudMatch ? (nikudMatch[2] ?? '').trim() : part
      const nikud = nikudMatch ? (nikudMatch[1] ?? null) : null
      const abbrevMatch = text.match(/^\(=([^)]+)\)\s*(.*)$/)
      return {
        nikud,
        text: abbrevMatch ? (abbrevMatch[2] ?? '').trim() : text,
        expansion: abbrevMatch ? (abbrevMatch[1] ?? null) : null,
      }
    })
}

function crossRef(entry: DictEntry): string | null {
  const m = (entry.definition ?? '').match(/^ראה גם:\s*(.+)$/)
  return m ? (m[1] ?? '').trim() : null
}

function isRedirect(entry: DictEntry): boolean {
  const raw = entry.definition ?? ''
  return raw.startsWith('REDIRECT') || raw.startsWith('הפניה')
}

function isHtml(entry: DictEntry): boolean {
  return (entry.definition ?? '').includes('<') && (entry.definition ?? '').includes('>')
}

function getContent(entry: DictEntry): DictEntryContent | null | undefined {
  return props.expandedEntries.get(entry.id)
}
</script>

<template>
  <div class="dict-results-page">
    <!-- States -->
    <div v-if="searching" class="dict-state">מחפש...</div>
    <div v-else-if="hasSearched && filteredResults.length === 0" class="dict-state">
      לא נמצאו תוצאות
    </div>

    <!-- Document -->
    <div v-else class="dict-document">
      <!-- ── Exact match entries ── -->
      <div v-for="group in exactGroups" :key="group.headword" class="dict-entry">
        <div class="dict-entry-head">
          <span class="dict-entry-hw">{{ group.headword }}</span>
        </div>

        <!-- Nikud variants section -->
        <div v-if="group.nikudVariants.length > 0" class="dict-nikud-section">
          <div class="dict-nikud-body">
            <template v-for="(v, i) in group.nikudVariants" :key="v"
              ><span class="dict-nikud-variant">{{ v }}</span
              ><template v-if="i < group.nikudVariants.length - 1"> &nbsp;</template></template
            >
          </div>
        </div>

        <!-- Grouped cross-references -->
        <div v-if="group.crossRefs.length > 0" class="dict-crossrefs">
          <span class="dict-crossrefs-label">ראה גם: </span>
          <button
            v-for="ref in group.crossRefs"
            :key="ref"
            class="dict-crossref-link"
            @click="emit('searchWord', ref)"
          >
            {{ ref }}
          </button>
        </div>

        <div v-for="entry in group.entries" :key="entry.id" class="dict-source-block">
          <div class="dict-source-label">
            <span class="dict-source-name">{{ entry.bookTitle }}</span>
            <button
              v-if="entry.bookId !== null"
              class="dict-open-btn"
              @click="emit('openInViewer', entry.id)"
            >
              <IconOpen20Regular />
            </button>
          </div>

          <div v-if="isRedirect(entry)" />

          <div v-else-if="getContent(entry) === null" class="dict-loading">טוען...</div>

          <template v-else-if="entry.type === 'abbrev'">
            <div v-for="(sense, si) in parseSenses(entry)" :key="si" class="dict-abbrev-sense">
              <span v-if="sense.expansion" class="dict-abbrev-expansion">{{
                sense.expansion
              }}</span>
              <span v-if="sense.text" class="dict-abbrev-rest">{{ sense.text }}</span>
            </div>
          </template>

          <div
            v-else-if="isHtml(entry) && getContent(entry)"
            class="dict-html-body"
            v-html="getContent(entry)!.html.replace(/\n/g, '<br>')"
          />
          <div
            v-else-if="isHtml(entry)"
            class="dict-html-body"
            v-html="(entry.definition ?? '').replace(/\n/g, '<br>')"
          />

          <template v-else>
            <div class="dict-senses-inline">
              <template v-for="(sense, si) in parseSenses(entry)" :key="si">
                <span v-if="si > 0" class="dict-sense-sep"> · </span>
                <span v-if="sense.nikud" class="dict-sense-nikud-paren">({{ sense.nikud }})</span>
                <span class="dict-sense-text">{{ sense.text }}</span>
              </template>
            </div>
          </template>
        </div>
      </div>

      <!-- ── Inflections / related forms ── -->
      <div v-if="inflectionWords.length > 0" class="dict-inflections">
        <span class="dict-inflections-label">הטיות</span>
        <div class="dict-inflections-body">
          <button
            v-for="w in inflectionWords"
            :key="w.headword"
            class="dict-crossref-link"
            @click="emit('searchWord', w.headword)"
          >
            {{ w.nikud ?? w.headword }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dict-results-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: var(--bg-primary);
  direction: rtl;
}

/* ── States ── */
.dict-state {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  color: var(--text-secondary);
}

/* ── Document ── */
.dict-document {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  padding: 10px 14px 24px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* ── Exact entry ── */
.dict-entry {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.dict-entry-head {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding-bottom: 3px;
  border-bottom: 1px solid var(--accent-color);
}
.dict-entry-hw {
  font-size: 16px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
}

/* ── Nikud variants ── */
.dict-nikud-section {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding-inline-start: 8px;
}
.dict-nikud-body {
  font-size: 13px;
  line-height: 1.5;
}
.dict-nikud-variant {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  white-space: nowrap;
}

/* ── Source block ── */
.dict-source-block {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-inline-start: 8px;
  border-inline-start: 2px solid color-mix(in srgb, var(--border-color) 70%, transparent);
}
.dict-source-label {
  display: flex;
  align-items: center;
  gap: 4px;
}
.dict-source-name {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  opacity: 0.8;
}
.dict-open-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
  border-radius: 2px;
  flex-shrink: 0;
}
.dict-open-btn:hover {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 10%, transparent);
}

/* ── Cross-references (grouped) ── */
.dict-crossrefs {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 2px 0;
  padding-inline-start: 8px;
}
.dict-crossrefs::after {
  content: '';
  flex: 1 0 0;
}
.dict-crossrefs-label {
  font-size: 12px;
  color: var(--text-secondary);
  flex-shrink: 0;
  width: 100%;
}
.dict-crossref-link {
  display: inline;
  color: var(--accent-color);
  font-weight: 600;
  font-size: 12px;
  text-decoration: underline;
  text-underline-offset: 2px;
  border: none;
  background: none;
  cursor: pointer;
  padding: 0 3px;
  border-radius: 0;
  white-space: nowrap;
  line-height: 1.6;
}
.dict-crossrefs-sep {
  color: var(--text-secondary);
}

/* ── Loading ── */
.dict-loading {
  font-size: 11px;
  color: var(--text-secondary);
}

/* ── Abbreviation ── */
.dict-abbrev-sense {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px;
}
.dict-abbrev-expansion {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.dict-abbrev-rest {
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── HTML body ── */
.dict-html-body {
  font-size: 12px;
  color: var(--text-primary);
  line-height: 1.55;
  text-align: justify;
}
.dict-html-body :deep(b) {
  font-weight: 700;
}
.dict-html-body :deep(big) {
  font-size: 1.05em;
  font-weight: 700;
  color: var(--accent-color);
}
.dict-html-body :deep(h3) {
  font-size: 1em;
  font-weight: 700;
  color: var(--accent-color);
  margin: 0 0 1px;
}
.dict-html-body :deep(small) {
  font-size: 0.85em;
  color: var(--text-secondary);
}
.dict-html-body :deep(span[dir='ltr']) {
  direction: ltr;
  display: inline-block;
  color: var(--text-secondary);
  font-style: italic;
}

/* ── Regular senses (inline) ── */
.dict-senses-inline {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
}
.dict-sense-sep {
  color: var(--text-secondary);
  margin: 0 1px;
}
.dict-sense-nikud-paren {
  font-size: 12px;
  color: var(--text-secondary);
  margin-inline-end: 2px;
}
.dict-sense-text {
  color: var(--text-primary);
}

/* ── Inflections section ── */
.dict-inflections {
  display: flex;
  flex-direction: column;
  gap: 3px;
  border-top: 1px solid var(--border-color);
  padding-top: 4px;
}
.dict-inflections-label {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.03em;
}
.dict-inflections-body {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 2px 0;
  padding-inline-start: 8px;
}
.dict-inflections-body::after {
  content: '';
  flex: 1 0 0;
}
</style>
