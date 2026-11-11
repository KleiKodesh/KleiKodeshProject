<script setup lang="ts">
/**
 * The book view toolbar's related-books dropdown.
 *
 * Lists every book in a fixed relationship to the current one - its base texts, targumim
 * and commentaries, by category, then the commentaries of each base text - and opens the
 * clicked one in a new tab at the line matching the current top line.
 *
 * Both the list and the target line are worked out by the service (Core's
 * SeforimDbQueries.GetRelatedBooks / ResolveRelatedTarget): the list is fetched when the
 * dropdown opens and dropped when it closes; a click is one call.
 */
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { IconLibrary16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useToolbarOverflowSubmenu } from '@/composables/useToolbarOverflowSubmenu'
import { getRelatedBooks, resolveRelatedTarget } from '@/webview-host/seforimApi'
import type { RelatedBook, RelatedBooksSection } from '@/webview-host/queries.types'
import { displayCommentaryLabel } from './commentary/commentaryLabelDisplay'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  bookId: number | undefined
  currentScrollLineIndex: number
  /** Renders as a row of the toolbar's "more" flyout, its panel a side submenu. */
  inOverflow?: boolean
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean] }>()

const paneNavigation = usePaneNavigation()
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
  if (justClosed.value) return
  setOpen(!isOpen.value)
}

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)
const submenuStyle = useToolbarOverflowSubmenu(() => !!props.inOverflow, isOpen, toggleButtonRef, dropdownRef)

// ── The list: fetched while open, dropped on close ──────────────────────────────

/** null while loading. */
const sections = ref<RelatedBooksSection[] | null>(null)
let loadToken = 0

watch(
  [isOpen, () => props.bookId],
  async ([open, bookId]) => {
    const token = ++loadToken
    sections.value = null
    if (!open || bookId == null) return
    const list = await getRelatedBooks(bookId).catch(() => ({ sections: [] }))
    if (token === loadToken) sections.value = list.sections
  },
)

function sectionKey(section: RelatedBooksSection): string {
  return section.commentariesOfBookId != null ? `of:${section.commentariesOfBookId}` : `label:${section.label ?? ''}`
}

function sectionCaption(section: RelatedBooksSection): string {
  return section.commentariesOfBookId != null
    ? t('bookView.related.commentariesOf', { title: section.commentariesOfTitle ?? '' })
    : displayCommentaryLabel(section.label ?? '')
}

// ── Opening a book ────────────────────────────────────────────────────────────

const openingBookId = ref<number | null>(null)

async function onBookClick(book: RelatedBook) {
  if (openingBookId.value != null || props.bookId == null) return
  openingBookId.value = book.bookId
  const fromBookId = props.bookId
  const topLineIndex = props.currentScrollLineIndex
  setOpen(false)
  try {
    // A failed lookup still opens the book (at its start) rather than nothing.
    const target = await resolveRelatedTarget(fromBookId, topLineIndex, book).catch(() => null)
    paneNavigation.openTab({
      route: '/book-view',
      title: book.title,
      bookId: book.bookId,
      openTocLineIndex: target ?? undefined,
    })
  } finally {
    openingBookId.value = null
  }
}
</script>

<template>
  <div class="related-books-wrapper" :class="{ 'overflow-host': inOverflow }">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen, 'overflow-row': inOverflow }"
      :role="inOverflow ? 'menuitem' : undefined"
      :title="$t('bookView.related.title')"
      @click="toggleOpen"
    >
      <IconLibrary16Regular />
      <span v-if="inOverflow">{{ $t('bookView.related.title') }}</span>
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="related-books-dropdown" :class="inOverflow ? 'overflow-submenu' : dropdownPositionClass" :style="submenuStyle">
      <div v-if="sections == null" class="loading-message">{{ $t('bookView.related.scanning') }}</div>
      <div v-else-if="sections.length === 0" class="empty-message">{{ $t('bookView.related.empty') }}</div>
      <template v-else v-for="section in sections" :key="sectionKey(section)">
        <div class="section-caption">{{ sectionCaption(section) }}</div>
        <button
          v-for="book in section.books"
          :key="book.bookId"
          class="book-row"
          :disabled="openingBookId === book.bookId"
          @click="onBookClick(book)"
        >
          {{ book.title }}
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
  /* The toolbar's button size, not a copy of what it happened to be: the overflow
     arithmetic charges every pinned control one of these, so a button that is its own size
     makes the toolbar believe it has room it does not have. */
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
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
  max-width: 300px;
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
  inset-inline-start: 0;
}

.dropdown-bottom {
  bottom: calc(100% + 4px);
  inset-inline-start: 0;
}

.dropdown-left {
  top: 0;
  inset-inline-end: calc(100% + 4px);
}

.dropdown-right {
  top: 0;
  inset-inline-start: calc(100% + 4px);
}

.empty-message {
  padding: 6px 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

.loading-message {
  padding: 6px 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── Section captions ── */
.section-caption {
  padding: 4px 10px 2px;
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
  height: 28px;
  padding: 0 10px;
  text-align: start;
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
