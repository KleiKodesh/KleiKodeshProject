<script setup lang="ts">
import { computed } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { WordPageData } from './DictionaryPage.vue'

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

// ── מחברת מנחם ───────────────────────────────────────────────────────────────

const menchemEntries = computed(() => {
  const seen = new Set<string>()
  return props.data.menchemRows.filter(row => {
    const key = `${row.lineId}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
})

function onMenchemClick(event: MouseEvent, row: { bookId: number; lineIndex: number }) {
  if (!event.ctrlKey) return
  event.preventDefault()
  tabStore.openTab({
    title: 'מחברת מנחם',
    route: '/book-view',
    bookId: row.bookId,
    openTocLineIndex: row.lineIndex,
  })
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

    <!-- מחברת מנחם — shown directly under the title -->
    <div v-if="menchemEntries.length" class="menchem-wrapper">
      <div class="menchem-scroll">
        <div
          v-for="(row, rowIndex) in menchemEntries"
          :key="rowIndex"
          class="menchem-entry"
          @click="onMenchemClick($event, row)"
        >
          <div class="menchem-label">מחברת מנחם — {{ row.word }}</div>
          {{ maybeFilter(row.text) }}
        </div>
      </div>
    </div>

    <!-- All definitions grouped by word form -->
    <section v-if="senseGroups.length" class="defs-section">
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

.menchem-wrapper {
  flex-shrink: 0;
  padding-bottom: 8px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}

.menchem-scroll {
  max-height: 80px;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.menchem-entry {
  font-size: 0.92em;
  line-height: 1.6;
  color: color-mix(in srgb, var(--text-primary) 80%, var(--text-secondary));
  overflow-wrap: break-word;
  cursor: default;
}
.menchem-entry:hover {
  color: var(--text-primary);
}

.menchem-label {
  display: block;
  font-size: 0.77em;
  font-weight: 700;
  color: var(--text-secondary);
  letter-spacing: 0.06em;
}
</style>
