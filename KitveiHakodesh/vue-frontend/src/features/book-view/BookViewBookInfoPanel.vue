<script setup lang="ts">
/**
 * The book-info content: what the library records ABOUT a book (authors, category, publication
 * place and date, licence, source...). Rendered both inside the toolbar's info dropdown and
 * inline in the overflow flyout, so the two faces show the same thing.
 *
 * Fetches when mounted and again when the book changes. Mounted only once someone opens it,
 * so most readers never pay for the query. Only rows that have a value are rendered.
 */
import { ref, computed, watch } from 'vue'
import { getBookMetadata } from '@/webview-host/seforimApi'
import type { BookMetadataRow } from '@/webview-host/queries.types'

const props = defineProps<{
  bookId: number | undefined
}>()

// ── Data ──────────────────────────────────────────────────────────────────────

const metadata = ref<BookMetadataRow | null>(null)
const loadedBookId = ref<number | null>(null)
const isLoading = ref(false)
const loadFailed = ref(false)

async function loadMetadata() {
  const bookId = props.bookId
  if (bookId == null || loadedBookId.value === bookId) return
  isLoading.value = true
  loadFailed.value = false
  try {
    const result = await getBookMetadata(bookId)
    // The book changed while this was in flight - its answer belongs to no one now.
    if (props.bookId !== bookId) return
    metadata.value = result ?? null
    loadedBookId.value = bookId
  } catch {
    if (props.bookId === bookId) loadFailed.value = true
  } finally {
    if (props.bookId === bookId) isLoading.value = false
  }
}

watch(
  () => props.bookId,
  () => {
    metadata.value = null
    loadedBookId.value = null
    isLoading.value = false
    loadFailed.value = false
    void loadMetadata()
  },
  { immediate: true },
)

// ── Rows ──────────────────────────────────────────────────────────────────────

/** Placeholder values the library uses for "not recorded" - showing them says nothing. */
const UNKNOWN_VALUES = new Set(['unknown'])

function meaningfulValues(values: (string | null | undefined)[]): string[] {
  return values
    .map((value) => value?.trim() ?? '')
    .filter((value) => value !== '' && !UNKNOWN_VALUES.has(value.toLowerCase()))
}

/** Percent-encoded URLs (Wikisource's Hebrew paths) are unreadable; show them decoded. */
function decodeUrlForDisplay(value: string): string {
  try {
    return decodeURI(value)
  } catch {
    return value
  }
}

interface BookInfoRow {
  label: string
  value: string
}

const infoRows = computed<BookInfoRow[]>(() => {
  const book = metadata.value
  if (!book) return []
  // The third element marks a row whose values are URLs: decoded, one per line.
  const candidates: [string, (string | null | undefined)[], boolean?][] = [
    ['שם הספר', [book.title]],
    // heRef is often identical to the title; repeating it adds nothing.
    ['כינוי', [book.heRef !== book.title ? book.heRef : null]],
    ['שם כולל', [book.collectiveTitle]],
    ['מחבר', book.authors],
    ['קטגוריה', [book.categoryTitle]],
    ['מקום הדפסה', book.publicationPlaces],
    ['שנת הדפסה', book.publicationDates],
    ['נושאים', book.topics],
    ['תיאור', [book.description ?? book.shortDescription]],
    ['זכויות יוצרים', book.licenses],
    ['מקור המהדורה', book.editionSources, true],
    ['מקור דיגיטלי', [book.sourceName]],
  ]
  return candidates
    .map(([label, values, isUrlList]) => ({
      label,
      value: isUrlList
        ? meaningfulValues(values).map(decodeUrlForDisplay).join('\n')
        : meaningfulValues(values).join(', '),
    }))
    .filter((row) => row.value !== '')
})
</script>

<template>
  <div class="book-info-panel">
    <div v-if="isLoading" class="book-info-message">טוען...</div>
    <div v-else-if="loadFailed" class="book-info-message">לא ניתן לטעון את פרטי הספר</div>
    <div v-else-if="infoRows.length === 0" class="book-info-message">אין פרטים נוספים על ספר זה</div>
    <dl v-else class="book-info-list">
      <div v-for="row in infoRows" :key="row.label" class="book-info-row">
        <dt>{{ row.label }}</dt>
        <dd>{{ row.value }}</dd>
      </div>
    </dl>
  </div>
</template>

<style scoped>
/* main.css sets `* { user-select: none }` on every element, so opting the panel's root
   out is not enough - each descendant has to be opted back in too. */
.book-info-panel,
.book-info-panel * {
  user-select: text;
  cursor: text;
}
.book-info-panel {
  text-align: right;
  font-size: 12px;
  color: var(--text-primary);
}
.book-info-list {
  margin: 0;
}
.book-info-row {
  padding: 4px 0;
}
.book-info-row + .book-info-row {
  border-top: 1px solid var(--border-color);
}
.book-info-row dt {
  color: var(--text-secondary);
  font-size: 11px;
}
.book-info-row dd {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.book-info-message {
  padding: 4px 0;
  color: var(--text-secondary);
}
</style>
