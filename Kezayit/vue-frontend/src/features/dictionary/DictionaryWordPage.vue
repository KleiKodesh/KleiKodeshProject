<script setup lang="ts">
import { computed } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { WordPageData } from './dictionaryTypes'

const props = defineProps<{ data: WordPageData; fontPx?: number }>()
const emit  = defineEmits<{ (e: 'select', headword: string): void }>()

const settings = useSettingsStore()
const tabStore  = useTabStore()

function maybeFilter(text: string): string {
  return settings.censorDivineNames ? censorDivineNames(text) : text
}

function stripHtml(source: string): string {
  return source.replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim()
}

// ── Definitions grouped by word form ─────────────────────────────────────────

const SOURCE_ORDER: Record<number, number> = { 5: 0, 1: 1, 6: 2, 2: 3, 4: 4, 3: 5 }

interface BookLocation {
  bookId:    number
  bookTitle: string
  lineIndex: number
}

interface SenseItem {
  text:         string
  sourceLabel:  string
  bookLocation: BookLocation | null  // non-null for seforim sources — enables Ctrl+click
}

interface SenseGroup {
  heading: string | null   // null = no heading (single group)
  items:   SenseItem[]
}

const senseGroups = computed((): SenseGroup[] => {
  // Sort dictionary senses by source priority
  const sorted = [...props.data.senses].sort(
    (a, b) => (SOURCE_ORDER[a.source_id ?? 99] ?? 99) - (SOURCE_ORDER[b.source_id ?? 99] ?? 99)
  )

  // Group everything by the word form (nikud if available, else plain headword)
  const map = new Map<string, SenseItem[]>()

  const addItem = (groupKey: string, item: SenseItem) => {
    if (!map.has(groupKey)) map.set(groupKey, [])
    map.get(groupKey)!.push(item)
  }

  // Dictionary senses — group key is nikud form or plain headword
  for (const row of sorted) {
    const key = row.nikud ?? row.headword
    addItem(key, { text: row.text, sourceLabel: row.source ?? '', bookLocation: null })
  }

  // Radak — group key is the headword (no nikud in Radak entries)
  for (const row of props.data.radak) {
    addItem(row.headword, { text: row.text, sourceLabel: 'רד"ק', bookLocation: null })
  }

  // מצודת ציון — group key is the matched word from the bold tag
  const seenMetzudat = new Set<string>()
  for (const row of props.data.metzudat) {
    const clean = stripHtml(row.definition)
    const deduplicationKey = `${row.word}::${clean}`
    if (seenMetzudat.has(deduplicationKey)) continue
    seenMetzudat.add(deduplicationKey)
    addItem(row.word, {
      text: clean,
      sourceLabel: row.bookTitle,
      bookLocation: { bookId: row.bookId, bookTitle: row.bookTitle, lineIndex: row.lineIndex },
    })
  }

  // מלבי"ם — group key is the matched word from the bold tag
  const seenMalbim = new Set<string>()
  for (const row of props.data.malbim) {
    const clean = stripHtml(row.definition)
    const deduplicationKey = `${row.word}::${clean}`
    if (seenMalbim.has(deduplicationKey)) continue
    seenMalbim.add(deduplicationKey)
    addItem(row.word, {
      text: clean,
      sourceLabel: row.bookTitle,
      bookLocation: { bookId: row.bookId, bookTitle: row.bookTitle, lineIndex: row.lineIndex },
    })
  }

  // Show headings only when there are multiple distinct word forms
  const showHeadings = map.size > 1
  return [...map.entries()].map(([heading, items]) => ({
    heading: showHeadings ? heading : null,
    items,
  }))
})

// ── Special sources (מחברת מנחם and הערוך) — shown after all definitions ──────

const specialEntries = computed((): SenseItem[] => {
  const items: SenseItem[] = []

  // מחברת מנחם
  const seenMenchem = new Set<string>()
  for (const row of props.data.menchemRows) {
    if (seenMenchem.has(`${row.lineId}`)) continue
    seenMenchem.add(`${row.lineId}`)
    items.push({
      text: row.text,
      sourceLabel: `מחברת מנחם — ${row.word}`,
      bookLocation: { bookId: row.bookId, bookTitle: 'מחברת מנחם', lineIndex: row.lineIndex },
      isSpecialSource: true,
    })
  }

  // ספר הערוך
  const seenAruch = new Set<string>()
  for (const row of props.data.aruchRows) {
    if (seenAruch.has(`${row.lineId}`)) continue
    seenAruch.add(`${row.lineId}`)
    items.push({
      text: row.text,
      sourceLabel: `הערוך — ${row.word}`,
      bookLocation: { bookId: row.bookId, bookTitle: 'ספר הערוך', lineIndex: row.lineIndex },
      isSpecialSource: true,
    })
  }

  return items
})

function onSourceClick(event: MouseEvent, location: BookLocation | null) {
  if (!event.ctrlKey || !location) return
  event.preventDefault()
  tabStore.openTab({
    title: location.bookTitle,
    route: '/book-view',
    bookId: location.bookId,
    openTocLineIndex: location.lineIndex,
  })
}

// ── Related words ─────────────────────────────────────────────────────────────

const allLinks = computed(() => {
  const seen = new Set<string>()
  const words: string[] = []
  for (const link of props.data.links) {
    if (!seen.has(link.word)) { seen.add(link.word); words.push(link.word) }
  }
  return words
})

const allRelated = computed(() => [
  ...props.data.synonyms,
  ...allLinks.value,
  ...props.data.variants,
  ...(props.data.ketivSuggestions ?? []),
  ...(props.data.levenshteinSuggestions ?? []),
])
</script>

<template>
  <article class="word-page" dir="rtl" :style="fontPx ? { fontSize: `${fontPx}px` } : undefined">

    <h1 class="word-title">{{ data.headword }}</h1>

    <!-- All definitions grouped by word form, followed by מחברת מנחם and הערוך -->
    <section v-if="senseGroups.length || specialEntries.length" class="defs-section">
      <div v-for="(group, groupIndex) in senseGroups" :key="groupIndex" class="def-group">
        <span v-if="group.heading" class="def-group-heading">{{ group.heading }}</span>
        <ol class="def-list">
          <li v-for="(item, itemIndex) in group.items" :key="itemIndex" class="def-item">
            {{ maybeFilter(item.text) }}<span
              v-if="item.sourceLabel"
              class="def-source"
              :class="{ 'def-source--link': item.bookLocation }"
              @click="onSourceClick($event, item.bookLocation)"
            > ({{ item.sourceLabel }})</span>
          </li>
        </ol>
      </div>
      <div
        v-for="(item, itemIndex) in specialEntries"
        :key="`special-${itemIndex}`"
        class="special-entry"
        @click="onSourceClick($event, item.bookLocation)"
      >
        <span class="special-entry-label">{{ item.sourceLabel }}</span>
        {{ maybeFilter(item.text) }}
      </div>
    </section>

    <!-- Related -->
    <section v-if="allRelated.length" class="related-section">
      <h2 class="section-title">קשורים</h2>
      <p class="related-line">
        <span v-for="(word, wordIndex) in allRelated" :key="word">
          <button class="word-link" @click="emit('select', word)">{{ word }}</button><span v-if="wordIndex < allRelated.length - 1" class="comma">, </span>
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
  font-size: 1.69em;
  font-weight: 700;
  margin: 0 0 8px;
  line-height: 1.2;
  flex-shrink: 0;
}

.section-title {
  font-size: 0.77em;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
  margin: 0 0 4px;
  text-transform: uppercase;
  flex-shrink: 0;
}

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
  margin-bottom: 8px;
}

.def-group-heading {
  display: block;
  font-size: 0.77em;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
  margin-bottom: 2px;
}

.def-list {
  margin: 0;
  padding-inline-start: 18px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.def-item {
  font-size: 0.92em;
  line-height: 1.6;
  color: color-mix(in srgb, var(--text-primary) 80%, var(--text-secondary));
  overflow-wrap: break-word;
}

.def-source {
  font-size: 0.85em;
  color: var(--text-secondary);
}

.def-source--link {
  cursor: pointer;
}
.def-source--link:hover {
  color: var(--accent-color);
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
  font-size: 0.92em;
  line-height: 1.6;
}

.comma { color: var(--text-secondary); }

.word-link {
  font-size: 0.92em;
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

.special-entry {
  font-size: 0.92em;
  line-height: 1.6;
  color: color-mix(in srgb, var(--text-primary) 80%, var(--text-secondary));
  overflow-wrap: break-word;
  cursor: default;
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}
.special-entry:first-of-type {
  margin-top: 8px;
  padding-top: 8px;
}
.special-entry:hover {
  color: var(--text-primary);
}

.special-entry-label {
  display: block;
  font-size: 0.77em;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
  margin-bottom: 2px;
}
</style>
