<script setup lang="ts">
/**
 * "ספרים קרובים" dropdown in the BookView toolbar.
 *
 * Shows all books linked to the current book (SOURCE / TARGUM / COMMENTARY
 * connection types) grouped by meaningful category (ראשונים, אחרונים, etc.
 * for commentary; מקורות / תרגומים for flat types). Clicking a book opens it
 * in a new tab, scrolled to the line that corresponds to the current top
 * visible line.
 *
 * Resolution order for the target line:
 *   1. Direct hit  — the current top line already has a link to that book.
 *   2. Forward scan — nearest line ahead that has a link.
 *   3. Backward scan — nearest line behind that has a link.
 *   4. Fallback — open the book at its beginning (lineIndex 0).
 */
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { IconLibrary16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { LineItem } from './lines/useBookViewLinesTable'

const props = defineProps<{
  bookId: number | undefined
  filterGroups: CommentaryGroup[]
  relatedBooksLoaded: boolean
  currentScrollLineIndex: number
  lines: LineItem[]
  disabled?: boolean
  onOpen?: () => void
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean] }>()

const tabStore = useTabStore()
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

// ── Open / close ──────────────────────────────────────────────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

function setOpen(value: boolean) {
  isOpen.value = value
  emit('open-change', value)
}

const { justClosed } = useDropdownClose(dropdownRef, () => setOpen(false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (props.disabled) return
  if (justClosed.value) return
  const opening = !isOpen.value
  setOpen(opening)
  if (opening) props.onOpen?.()
}

// ── Book list — grouped by category (ראשונים / אחרונים / etc.) ───────────────
//
// For connection types that carry a subSectionLabel (COMMENTARY, OTHER), we use
// the subSectionLabel (the period/category) as the section header instead of the
// generic sectionLabel ("מפרשים"). For flat types (SOURCE, TARGUM, REFERENCE)
// there is no subSectionLabel, so we fall back to sectionLabel as before.

interface RelatedBook {
  bookId: number
  bookTitle: string
}

interface RelatedBooksSection {
  label: string
  books: RelatedBook[]
}

const dropdownPositionClass = computed(() => {
  return `dropdown-${toolbarPosition.value}`
})

const sections = computed<RelatedBooksSection[]>(() => {
  const sectionMap = new Map<string, RelatedBook[]>()
  const sectionOrder: string[] = []
  const seen = new Set<number>()

  for (const group of props.filterGroups) {
    // Use the specific category label (ראשונים, אחרונים…) when available,
    // otherwise fall back to the connection-type section label (מקורות, תרגומים…).
    const label = group.subSectionLabel || group.sectionLabel || ''
    if (!sectionMap.has(label)) {
      sectionMap.set(label, [])
      sectionOrder.push(label)
    }
    if (seen.has(group.bookId)) continue
    seen.add(group.bookId)
    sectionMap.get(label)!.push({ bookId: group.bookId, bookTitle: group.bookTitle })
  }

  return sectionOrder
    .map((label) => ({ label, books: sectionMap.get(label)! }))
    .filter((section) => section.books.length > 0)
})

// ── Opening a book ────────────────────────────────────────────────────────────

const openingBookId = ref<number | null>(null)

async function resolveTargetLineIndex(targetBookId: number): Promise<number | undefined> {
  if (props.bookId == null) return undefined

  // Find the line id for the current top visible line
  const topLine = props.lines.find((l) => l.lineIndex === props.currentScrollLineIndex)
  if (topLine == null) return undefined

  // 1. Direct hit — does the current top line already link to this book?
  const directRows = await query<{ targetLineId: number; lineIndex: number }>(
    SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK,
    [topLine.id, targetBookId],
  )
  if (directRows.length) return directRows[0]!.lineIndex

  // 2. Forward scan — nearest line ahead with a link to this book
  const forwardRows = await query<{ id: number; lineIndex: number }>(
    SQL.GET_NEXT_SECTION_WITH_COMMENTARY,
    [props.bookId, targetBookId, props.currentScrollLineIndex],
  )
  if (forwardRows.length) {
    // Resolve the target line in the commentary book for that source line
    const targetRows = await query<{ targetLineId: number; lineIndex: number }>(
      SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK,
      [forwardRows[0]!.id, targetBookId],
    )
    if (targetRows.length) return targetRows[0]!.lineIndex
  }

  // 3. Backward scan — nearest line behind with a link to this book
  const backwardRows = await query<{ id: number; lineIndex: number }>(
    SQL.GET_PREV_SECTION_WITH_COMMENTARY,
    [props.bookId, targetBookId, props.currentScrollLineIndex],
  )
  if (backwardRows.length) {
    const targetRows = await query<{ targetLineId: number; lineIndex: number }>(
      SQL.GET_LINK_TARGET_FOR_SOURCE_LINE_AND_BOOK,
      [backwardRows[0]!.id, targetBookId],
    )
    if (targetRows.length) return targetRows[0]!.lineIndex
  }

  // 4. Fallback — open at the beginning
  return undefined
}

async function onBookClick(book: RelatedBook) {
  if (openingBookId.value != null) return
  openingBookId.value = book.bookId
  setOpen(false)
  try {
    const targetLineIndex = await resolveTargetLineIndex(book.bookId)
    tabStore.openTab({
      route: '/book-view',
      title: book.bookTitle,
      bookId: book.bookId,
      openTocLineIndex: targetLineIndex,
    })
  } finally {
    openingBookId.value = null
  }
}
</script>

<template>
  <div class="related-books-wrapper">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen }"
      :disabled="disabled"
      title="ספרים קרובים"
      @click="toggleOpen"
    >
      <IconLibrary16Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="related-books-dropdown" :class="dropdownPositionClass">
      <div v-if="!relatedBooksLoaded" class="loading-message">בסריקה...</div>
      <div v-else-if="sections.length === 0" class="empty-message">אין ספרים קרובים</div>
      <template v-else v-for="section in sections" :key="section.label">
        <div class="section-caption">{{ section.label }}</div>
        <button
          v-for="book in section.books"
          :key="book.bookId"
          class="book-row"
          :disabled="openingBookId === book.bookId"
          @click="onBookClick(book)"
        >
          {{ book.bookTitle }}
        </button>
      </template>
    </div>
  </div>
</template>

<style scoped>
.related-books-wrapper {
  position: relative;
}

/* ── Toggle button — inherits toolbar button styles from main.css ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button.active {
  color: var(--accent-color);
}

/* ── Dropdown panel ── */
.related-books-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 280px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Positioning based on toolbar position ── */
.dropdown-top {
  top: calc(100% + 4px);
  right: 0;
}

.dropdown-bottom {
  bottom: calc(100% + 4px);
  right: 0;
}

.dropdown-left {
  top: 0;
  left: calc(100% + 4px);
}

.dropdown-right {
  top: 0;
  right: calc(100% + 4px);
}

.empty-message {
  padding: 8px 12px;
  font-size: 12px;
  color: var(--text-secondary);
}

.loading-message {
  padding: 8px 12px;
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── Section captions ── */
.section-caption {
  padding: 6px 12px 2px;
  font-size: 10px;
  color: var(--text-secondary);
  user-select: none;
  pointer-events: none;
}
.section-caption:not(:first-child) {
  border-top: 1px solid var(--border-color);
  margin-top: 2px;
  padding-top: 6px;
}

/* ── Book rows ── */
.book-row {
  display: block;
  width: 100%;
  height: 32px;
  padding: 0 12px;
  text-align: right;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-radius: 0;
}
.book-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.book-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.book-row:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
