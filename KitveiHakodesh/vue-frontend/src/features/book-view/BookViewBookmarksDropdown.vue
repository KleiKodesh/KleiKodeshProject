<script setup lang="ts">
/**
 * Bookmarks dropdown in the BookView toolbar - places the reader marked in this book.
 *
 * Absent until the book has a bookmark: bookmarks are CREATED from the lines' context menu
 * or Ctrl+Shift+D, which open this dropdown on the new row in rename mode (its suggested name
 * the TOC path). A bookmark points at an exact place - the reader's selection, or the
 * word at the top of the view - not just a line, since a line can run for pages.
 * The book's bookmarks are listed in reading order with their TOC path and text: clicking
 * one scrolls there, the pencil renames it inline, the bin deletes it. The toolbar icon is
 * filled while the top line is bookmarked.
 */
import { ref, shallowRef, computed, watch, watchEffect, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconBookmark16Regular,
  IconBookmark16Filled,
  IconEdit16Regular,
  IconDelete16Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useOverflowSubmenu } from './useOverflowSubmenu'
import { loadBookBookmarks, loadedBookBookmarks, updateBookBookmarks, type BookBookmark } from '@/stores/bookBookmarks'
import type { LinesTable } from './lines/linesTable'
import type { BookViewLinesBookmarkPoint } from './lines/useBookViewLinesBookmarkPoint'

const props = defineProps<{
  bookId: number | undefined
  currentScrollLineIndex: number
  lines: LinesTable
  /** The default name for a bookmark on this line - its TOC path. */
  labelForLine?: (lineIndex: number) => string
  /** Where a bookmark made now points: the selection, or the top of the view. */
  readBookmarkPoint?: () => BookViewLinesBookmarkPoint | null
  /** Renders as a row of the toolbar's "more" flyout, its panel a side submenu. */
  inOverflow?: boolean
}>()

const emit = defineEmits<{ goToBookmark: [bookmark: BookBookmark]; presenceChange: [present: boolean] }>()

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
const submenuStyle = useOverflowSubmenu(() => !!props.inOverflow, isOpen, toggleButtonRef, dropdownRef)

// ── The book's list ───────────────────────────────────────────────────────────
//
// Read from the store's shared per-book list, so the other pane and the flyout's copy of
// this dropdown see a change the moment it is saved. Every change goes through
// updateBookBookmarks, which edits the STORED list - never this display copy.

watch(
  () => props.bookId,
  (bookId) => {
    if (bookId != null) void loadBookBookmarks(bookId).catch(() => {})
  },
  { immediate: true },
)

// Kept on the previous book's list until the next one has loaded, so switching between
// two bookmarked books does not flash the control away and back for a frame.
const bookmarks = shallowRef<BookBookmark[]>([])
watchEffect(() => {
  if (props.bookId == null) { bookmarks.value = []; return }
  const loaded = loadedBookBookmarks(props.bookId)
  if (loaded) bookmarks.value = loaded
})

async function update(change: (stored: BookBookmark[]) => BookBookmark[]): Promise<boolean> {
  if (props.bookId == null) return false
  try {
    await updateBookBookmarks(props.bookId, change)
    return true
  } catch (error) {
    console.error('Saving bookmarks failed', error)
    return false
  }
}

/**
 * Whether the control is on the toolbar: only once the book has a bookmark - or while it
 * is open, so a first bookmark being named does not vanish under the reader. Reported to
 * the toolbar, which counts the room only of controls that are there.
 */
const isPresent = computed(() => bookmarks.value.length > 0 || isOpen.value)
watch(isPresent, (present) => emit('presenceChange', present), { immediate: true })

const currentLine = computed(() => props.lines.at(props.currentScrollLineIndex))

const currentLineBookmarked = computed(() =>
  currentLine.value != null && bookmarks.value.some((b) => b.lineId === currentLine.value!.id),
)

/** A bookmark for where the reader is now, or null when there is no real line to mark yet. */
function bookmarkForPoint(point: BookViewLinesBookmarkPoint | null): BookBookmark | null {
  const line = props.lines.at(point?.lineIndex ?? props.currentScrollLineIndex)
  // A negative id is a placeholder for a line whose real id is not known yet.
  if (line == null || line.id <= 0) return null
  const tocPath = props.labelForLine?.(line.lineIndex).trim() || undefined
  return {
    id: Date.now(),
    lineId: line.id,
    lineIndex: line.lineIndex,
    letterOffset: point?.letterOffset,
    selectionLetterCount: point?.selectionLetterCount,
    label: tocPath ?? `${line.lineIndex + 1}`,
    tocPath,
    snippet: point?.snippet,
  }
}

function deleteBookmark(bookmark: BookBookmark) {
  if (editingId.value === bookmark.id) editingId.value = null
  void update((stored) => stored.filter((b) => b.id !== bookmark.id))
}

function goToBookmark(bookmark: BookBookmark) {
  isOpen.value = false
  emit('goToBookmark', bookmark)
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
  if (!open) commitRename()
})

/**
 * The context menu's and Ctrl+Shift+D's entry. The point is read BEFORE anything opens -
 * opening moves focus, and the selection must still be the reader's; a caller that opens
 * something first (the toolbar's flyout) reads it earlier and passes it in. With no line
 * to mark nothing opens at all. Then, like a browser's "add bookmark", the dropdown opens on
 * the new row with its name selected: type a name, or Enter keeps the suggested TOC path.
 */
async function createBookmark(point: BookViewLinesBookmarkPoint | null = props.readBookmarkPoint?.() ?? null) {
  const bookmark = bookmarkForPoint(point)
  if (bookmark == null) {
    // The toolbar counted the control as present ahead of this; set it straight.
    emit('presenceChange', isPresent.value)
    return
  }
  isOpen.value = true
  if (!(await update((stored) => [...stored, bookmark]))) return
  await startRename(bookmark)
}

defineExpose({ createBookmark })
</script>

<template>
  <div v-if="isPresent" class="bookmarks-wrapper" :class="{ 'overflow-host': inOverflow }">
    <button
      ref="toggleButtonRef"
      class="toggle-button"
      :class="{ active: isOpen, marked: currentLineBookmarked, 'overflow-row': inOverflow }"
      :role="inOverflow ? 'menuitem' : undefined"
      :title="`${$t('bookView.bookmarks.title')} (Ctrl+Shift+D)`"
      :disabled="bookId == null"
      @click="toggleOpen"
    >
      <IconBookmark16Filled v-if="currentLineBookmarked" />
      <IconBookmark16Regular v-else />
      <span v-if="inOverflow">{{ $t('bookView.bookmarks.title') }}</span>
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="bookmarks-dropdown" :class="inOverflow ? 'overflow-submenu' : dropdownPositionClass" :style="submenuStyle">
      <div v-if="bookmarks.length === 0" class="empty-message">{{ $t('bookView.bookmarks.empty') }}</div>
      <div v-else class="bookmark-list">
        <div
          v-for="bookmark in bookmarks"
          :key="bookmark.id"
          class="bookmark-row"
          :class="{ current: bookmark.lineId === currentLine?.id, editing: editingId === bookmark.id }"
        >
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

/* ── Dropdown panel - same sheet as the toolbar's other dropdowns ── */
.bookmarks-dropdown {
  position: absolute;
  display: flex;
  flex-direction: column;
  min-width: 180px;
  max-width: 260px;
  width: max-content;
  max-height: 320px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  overflow: hidden;
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

/* Only while a first bookmark failed to save - the dropdown is not shown on a book without any. */
.empty-message {
  padding: 6px 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── List ── */
.bookmark-list {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.bookmark-row {
  position: relative;
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  padding: 3px 10px;
}
.bookmark-row.current .bookmark-name {
  color: var(--accent-color);
}
.bookmark-row:hover {
  background: var(--hover-bg);
}

.bookmark-body {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  flex: 1;
  min-width: 0;
  height: auto;
  padding: 0;
  text-align: start;
  line-height: 1.3;
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
  font-size: 12px;
  color: var(--text-primary);
}
.bookmark-toc,
.bookmark-snippet {
  font-size: 10.5px;
  color: var(--text-secondary);
}

/* Overlaid on the row's end rather than given room of their own, so the panel is only as
   wide as the bookmarks' text; the backdrop matches the row's hover tint over the sheet. */
.row-actions {
  position: absolute;
  top: 0;
  bottom: 0;
  inset-inline-end: 0;
  display: flex;
  align-items: center;
  padding-inline: 4px;
  /* Solid: the row's hover tint is translucent, so it is laid over the sheet's own colour. */
  background: linear-gradient(var(--hover-bg), var(--hover-bg)), var(--bg-secondary);
  opacity: 0;
}
.bookmark-row:hover .row-actions,
.row-actions:focus-within {
  opacity: 1;
}
.row-action {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
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
  width: 13px;
  height: 13px;
}

.rename-input {
  flex: 1;
  min-width: 0;
  height: 22px;
  padding: 0 6px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--input-bg-focus);
  border: 1px solid var(--accent-color);
  border-radius: 3px;
  outline: none;
}
</style>
