<script setup lang="ts">
/**
 * Bookmarks dropdown in the BookView toolbar - places the reader marked in this book.
 *
 * A header with the count, then a "create bookmark" button for the current top line (the
 * new row opens in rename mode, its suggested name the TOC path). Below it the book's
 * bookmarks in reading order, each with its TOC path and the marked line's opening words:
 * clicking one scrolls there, the pencil renames it inline, the bin deletes it. A line may
 * carry several bookmarks under different names. The toolbar icon is filled while the top
 * line is bookmarked.
 */
import { ref, computed, watch, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconBookmark16Regular,
  IconBookmark16Filled,
  IconBookmark20Regular,
  IconBookmarkAdd20Regular,
  IconEdit16Regular,
  IconDelete16Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import { readBookBookmarks, updateBookBookmarks, type BookBookmark } from '@/stores/bookBookmarks'
import type { LinesTable } from './lines/linesTable'

const props = defineProps<{
  bookId: number | undefined
  currentScrollLineIndex: number
  lines: LinesTable
  /** The default name for a bookmark on this line - its TOC path. */
  labelForLine?: (lineIndex: number) => string
}>()

const emit = defineEmits<{ goToBookmark: [lineId: number, lineIndex: number] }>()

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(dropdownRef, () => { isOpen.value = false }, {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
}

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)

// ── The book's list ───────────────────────────────────────────────────────────
//
// A display copy only. Every change goes through updateBookBookmarks, which edits the
// STORED list, so a copy that is stale (another pane changed it, or the first read has
// not landed yet) can never be written back over newer bookmarks.

const bookmarks = ref<BookBookmark[]>([])

async function reload() {
  const bookId = props.bookId
  if (bookId == null) return
  const loaded = await readBookBookmarks(bookId).catch(() => null)
  if (loaded && bookId === props.bookId) bookmarks.value = loaded
}

watch(
  () => props.bookId,
  () => {
    bookmarks.value = []
    void reload()
  },
  { immediate: true },
)

async function update(change: (stored: BookBookmark[]) => BookBookmark[]) {
  const bookId = props.bookId
  if (bookId == null) return
  const saved = await updateBookBookmarks(bookId, change)
  // The book may have changed while the write was in flight; its list is not this one.
  if (bookId === props.bookId) bookmarks.value = saved
}

const currentLine = computed(() => props.lines.at(props.currentScrollLineIndex))

const currentLineBookmarked = computed(() =>
  currentLine.value != null && bookmarks.value.some((b) => b.lineId === currentLine.value!.id),
)

/** How much of the marked line is kept to show under the bookmark's name. */
const SNIPPET_LENGTH = 80

function lineSnippet(html: string | null): string | undefined {
  if (!html) return undefined
  const text = new DOMParser().parseFromString(html, 'text/html').body.textContent ?? ''
  return text.replace(/\s+/g, ' ').trim().slice(0, SNIPPET_LENGTH) || undefined
}

async function addBookmark() {
  const line = currentLine.value
  // A negative id is a placeholder for a line whose real id is not known yet.
  if (line == null || line.id <= 0) return
  const tocPath = props.labelForLine?.(line.lineIndex).trim() || undefined
  const label = tocPath ?? `${line.lineIndex + 1}`
  const bookmark: BookBookmark = {
    id: Date.now(), lineId: line.id, lineIndex: line.lineIndex, label, tocPath, snippet: lineSnippet(line.content),
  }
  // Like a browser's "add bookmark": the new row opens with its name field selected, so the
  // reader names it on the spot or presses Enter to keep the suggested TOC path.
  await update((stored) => [...stored, bookmark])
  await startRename(bookmark)
}

function deleteBookmark(bookmark: BookBookmark) {
  if (editingId.value === bookmark.id) editingId.value = null
  void update((stored) => stored.filter((b) => b.id !== bookmark.id))
}

function goToBookmark(bookmark: BookBookmark) {
  isOpen.value = false
  emit('goToBookmark', bookmark.lineId, bookmark.lineIndex)
}

// ── Inline rename ─────────────────────────────────────────────────────────────

const editingId = ref<number | null>(null)
const editingText = ref('')
const renameInputRef = ref<HTMLInputElement[] | null>(null)

async function startRename(bookmark: BookBookmark) {
  editingId.value = bookmark.id
  editingText.value = bookmark.label
  await nextTick()
  renameInputRef.value?.[0]?.select()
}

function commitRename() {
  const id = editingId.value
  if (id == null) return
  editingId.value = null
  const label = editingText.value.trim()
  if (!label) return
  void update((stored) => stored.map((b) => (b.id === id ? { ...b, label } : b)))
}

watch(isOpen, (open) => {
  // Opening re-reads, so a bookmark made in the other pane shows up here too.
  if (open) void reload()
  else commitRename()
})

/** The keyboard shortcut's entry: open the dropdown and create a bookmark on the top line. */
async function createBookmark() {
  if (props.bookId == null) return
  isOpen.value = true
  await nextTick()
  await addBookmark()
}

defineExpose({ createBookmark })
</script>

<template>
  <div class="bookmarks-wrapper">
    <button
      ref="toggleButtonRef"
      class="toggle-button"
      :class="{ active: isOpen, marked: currentLineBookmarked }"
      :title="`${$t('bookView.bookmarks.title')} (Ctrl+Shift+D)`"
      :disabled="bookId == null"
      @click="toggleOpen"
    >
      <IconBookmark16Filled v-if="currentLineBookmarked" />
      <IconBookmark16Regular v-else />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="bookmarks-dropdown" :class="dropdownPositionClass">
      <div class="dropdown-header">
        <span class="header-title">{{ $t('bookView.bookmarks.title') }}</span>
        <span v-if="bookmarks.length" class="header-count">{{ bookmarks.length }}</span>
      </div>

      <button class="create-button" :disabled="currentLine == null" @click="addBookmark">
        <IconBookmarkAdd20Regular />
        <span>{{ $t('bookView.bookmarks.add') }}</span>
        <kbd class="create-shortcut">Ctrl+Shift+D</kbd>
      </button>

      <div v-if="bookmarks.length === 0" class="empty-state">
        <IconBookmark20Regular class="empty-icon" />
        <span>{{ $t('bookView.bookmarks.empty') }}</span>
      </div>

      <div v-else class="bookmark-list">
        <div
          v-for="bookmark in bookmarks"
          :key="bookmark.id"
          class="bookmark-row"
          :class="{ current: bookmark.lineId === currentLine?.id, editing: editingId === bookmark.id }"
        >
          <IconBookmark16Filled class="row-icon" />
          <input
            v-if="editingId === bookmark.id"
            ref="renameInputRef"
            v-model="editingText"
            class="rename-input"
            @keydown.enter.prevent="commitRename"
            @keydown.esc.stop.prevent="editingId = null"
            @blur="commitRename"
          />
          <button
            v-else
            class="bookmark-body"
            :title="[bookmark.label, bookmark.tocPath !== bookmark.label ? bookmark.tocPath : null, bookmark.snippet].filter(Boolean).join('\n')"
            @click="goToBookmark(bookmark)"
          >
            <span class="bookmark-name">{{ bookmark.label }}</span>
            <span v-if="bookmark.tocPath && bookmark.tocPath !== bookmark.label" class="bookmark-toc">{{ bookmark.tocPath }}</span>
            <span v-if="bookmark.snippet" class="bookmark-snippet">{{ bookmark.snippet }}</span>
          </button>
          <div v-if="editingId !== bookmark.id" class="row-actions">
            <button class="row-action" :title="$t('bookView.bookmarks.rename')" @click="startRename(bookmark)">
              <IconEdit16Regular />
            </button>
            <button class="row-action danger" :title="$t('bookView.bookmarks.delete')" @click="deleteBookmark(bookmark)">
              <IconDelete16Regular />
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bookmarks-wrapper {
  position: relative;
}

/* ── Toggle button — the toolbar's button size, which the overflow arithmetic charges it ── */
.toggle-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
.toggle-button.active,
.toggle-button.marked {
  color: var(--accent-color);
}
.toggle-button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* ── Dropdown panel ── */
.bookmarks-dropdown {
  position: absolute;
  display: flex;
  flex-direction: column;
  width: 280px;
  max-width: 300px;
  max-height: 320px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18), 0 1px 3px rgba(0, 0, 0, 0.1);
  z-index: 100;
  overflow: hidden;
  animation: dropdown-in 120ms ease-out;
}
@keyframes dropdown-in {
  from { opacity: 0; transform: translateY(-4px); }
}
@media (prefers-reduced-motion: reduce) {
  .bookmarks-dropdown { animation: none; }
}

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

/* ── Header ── */
.dropdown-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px 4px;
  flex-shrink: 0;
}
.header-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.header-count {
  min-width: 18px;
  height: 18px;
  padding: 0 6px;
  border-radius: 9px;
  font-size: 11px;
  line-height: 18px;
  text-align: center;
  color: var(--accent-color);
  background: var(--accent-bg-light);
}

/* ── Create button ── */
.create-button {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  margin: 4px 8px 8px;
  padding: 0 10px;
  flex-shrink: 0;
  font-size: 12px;
  font-weight: 500;
  color: var(--accent-color);
  background: var(--accent-bg-light);
  border-radius: 6px;
  transition: background 120ms;
}
.create-button:not(:disabled):hover {
  background: var(--accent-bg);
}
.create-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.create-button svg {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.create-shortcut {
  margin-inline-start: auto;
  font-family: inherit;
  font-size: 10px;
  opacity: 0.7;
  direction: ltr;
}

/* ── Empty state ── */
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 12px 16px 18px;
  font-size: 12px;
  color: var(--text-secondary);
  text-align: center;
}
.empty-icon {
  width: 28px;
  height: 28px;
  opacity: 0.35;
}

/* ── List ── */
.bookmark-list {
  overflow-y: auto;
  padding: 0 4px 6px;
  border-top: 1px solid var(--border-color);
  padding-top: 4px;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.bookmark-row {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 6px;
  transition: background 100ms;
}
.bookmark-row:hover {
  background: var(--hover-bg);
}
/* The bookmark the reader is sitting on: an accent ribbon at the row's start edge. */
.bookmark-row.current::before {
  content: '';
  position: absolute;
  inset-inline-start: 0;
  top: 6px;
  bottom: 6px;
  width: 3px;
  border-radius: 2px;
  background: var(--accent-color);
}

.row-icon {
  width: 14px;
  height: 14px;
  margin-top: 2px;
  flex-shrink: 0;
  color: var(--accent-color);
  opacity: 0.55;
}
.bookmark-row.current .row-icon {
  opacity: 1;
}

.bookmark-body {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 1px;
  flex: 1;
  min-width: 0;
  padding: 0;
  text-align: start;
  background: none;
  border-radius: 0;
}
.bookmark-body:hover {
  background: none;
}
.bookmark-name,
.bookmark-toc,
.bookmark-snippet {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.bookmark-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-primary);
}
.bookmark-toc {
  font-size: 11px;
  color: var(--accent-color);
}
.bookmark-snippet {
  font-size: 11px;
  color: var(--text-secondary);
  font-family: var(--text-font);
}

.row-actions {
  display: flex;
  gap: 2px;
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 100ms;
}
.bookmark-row:hover .row-actions,
.row-actions:focus-within {
  opacity: 1;
}
.row-action {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 4px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.row-action:hover {
  color: var(--text-primary);
  background: var(--control-bg-hover);
}
.row-action.danger:hover {
  color: var(--status-danger);
}
.row-action svg {
  width: 14px;
  height: 14px;
}

.rename-input {
  flex: 1;
  min-width: 0;
  height: 26px;
  padding: 0 8px;
  font-size: 13px;
  color: var(--text-primary);
  background: var(--input-bg-focus);
  border: 1px solid var(--accent-color);
  border-radius: 4px;
  outline: none;
  box-shadow: 0 0 0 3px var(--accent-bg-light);
}
</style>
