<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { WindowedScroller, type WindowedScrollerApi } from 'vue-windowed-scroller'
import { IconAdd16Regular, IconEdit16Regular, IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import { useI18n } from 'vue-i18n'
import { PDF_KEYS, withKey } from './usePdfReaderShortcuts'
import { loadBookBookmarks, loadedBookBookmarks, updateBookBookmarks, type BookBookmark } from '@/stores/bookBookmarks'

/**
 * The PDF's bookmarks - the app's bookmarks store (stores/bookBookmarks), keyed `pdf:<path>`,
 * so they last across tabs and sessions like a book's. Add marks the top of the view (page +
 * point) and opens the new row's name for typing; click jumps, pencil renames, bin deletes.
 */
const props = defineProps<{
  owner: string
  pageNumber: number
  /** Where the view is now: page and the y (PDF units) at the top of the view. */
  readLocation: () => { pageNumber: number; top?: number } | null
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
}>()
const emit = defineEmits<{ select: [bookmark: BookBookmark] }>()
const { t } = useI18n()

watch(() => props.owner, (owner) => void loadBookBookmarks(owner).catch(() => {}), { immediate: true })
const bookmarks = computed(() => loadedBookBookmarks(props.owner) ?? [])

function update(change: (stored: BookBookmark[]) => BookBookmark[]) {
  return updateBookBookmarks(props.owner, change).then(() => true, (error) => {
    console.error('Saving bookmarks failed', error)
    return false
  })
}

async function add() {
  const loc = props.readLocation()
  if (!loc) return
  const label = t('pdfReader.panel.page', { n: props.labelOf?.(loc.pageNumber) ?? loc.pageNumber })
  const bookmark: BookBookmark = {
    id: Date.now(),
    lineId: loc.pageNumber,
    lineIndex: loc.pageNumber - 1,
    pdfTop: loc.top,
    label,
    tocPath: label,
  }
  if (await update((stored) => [...stored, bookmark])) void startRename(bookmark)
}

const editingId = ref<number | null>(null)
const editingText = ref('')
const scroller = ref<WindowedScrollerApi | null>(null)
/** The rename box takes focus when it mounts (its row may mount later, once landed on). */
let focusRenameBox = false
function setRenameBox(el: unknown) {
  // (No instanceof: the panel may be drawn in another window, with its own HTMLElement.)
  if (!el || !focusRenameBox) return
  focusRenameBox = false
  const input = el as HTMLInputElement
  // After v-model has filled it (its mounted hook runs after this ref).
  void nextTick(() => {
    input.focus()
    input.select()
  })
}
async function startRename(bookmark: BookBookmark) {
  editingId.value = bookmark.id
  editingText.value = bookmark.label
  focusRenameBox = true
  // The row may be outside the scroller's window (a new bookmark at the end): land on it.
  await nextTick()
  const index = bookmarks.value.findIndex((b) => b.id === bookmark.id)
  if (index >= 0 && scroller.value && !scroller.value.isRowFullyVisible(index)) void scroller.value.scrollToIndex(index, { align: 'end' })
}
function commitRename() {
  const id = editingId.value
  if (id == null) return
  editingId.value = null
  const label = editingText.value.trim()
  if (label) void update((stored) => stored.map((b) => (b.id === id ? { ...b, label } : b)))
}
function remove(bookmark: BookBookmark) {
  if (editingId.value === bookmark.id) editingId.value = null
  void update((stored) => stored.filter((b) => b.id !== bookmark.id))
}

defineExpose({ add })
</script>

<template>
  <div class="pdf-bookmarks">
    <div v-if="bookmarks.length === 0" class="list empty">{{ $t('pdfReader.panel.noBookmarks') }}</div>
    <WindowedScroller v-else ref="scroller" class="list" thin :count="bookmarks.length" :row-source-version="bookmarks">
      <template #row="{ index }">
      <div
        v-for="bookmark in bookmarks.slice(index, index + 1)"
        :key="bookmark.id"
        class="row"
        :class="{ current: bookmark.lineId === pageNumber }"
      >
        <input
          v-if="editingId === bookmark.id"
          :ref="setRenameBox"
          v-model="editingText"
          class="rename-input"
          @keydown.enter.prevent="commitRename"
          @keydown.esc.stop.prevent="editingId = null"
          @blur="commitRename"
        />
        <button v-else type="button" class="body" :title="bookmark.label" @click="emit('select', bookmark)" @dblclick="startRename(bookmark)">
          <span class="name">{{ bookmark.label }}</span>
          <span v-if="bookmark.tocPath && bookmark.tocPath !== bookmark.label" class="sub">{{ bookmark.tocPath }}</span>
        </button>
        <div v-if="editingId !== bookmark.id" class="row-actions">
          <button type="button" class="row-action" :title="$t('pdfReader.panel.rename')" @click="startRename(bookmark)"><IconEdit16Regular /></button>
          <button type="button" class="row-action danger" :title="$t('pdfReader.panel.delete')" @click="remove(bookmark)"><IconDelete16Regular /></button>
        </div>
      </div>
      </template>
    </WindowedScroller>
    <div class="footer">
      <button type="button" class="add" :title="withKey($t('pdfReader.panel.addBookmark'), PDF_KEYS.addBookmark)" @click="add">
        <IconAdd16Regular />
        <span>{{ $t('pdfReader.panel.addBookmark') }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.pdf-bookmarks {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.pdf-bookmarks > .windowed-scroller,
.list.empty {
  flex: 1;
  min-height: 0;
}
.empty {
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.row {
  position: relative;
  display: flex;
  align-items: center;
  min-height: 28px;
  padding: 3px 10px;
}
.row:hover {
  background: var(--hover-bg);
}
.row.current .name {
  color: var(--accent-color);
}
.body {
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
.body:hover {
  background: none;
}
.name,
.sub {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.name {
  font-size: 12px;
  color: var(--text-primary);
}
.sub {
  font-size: 10.5px;
  color: var(--text-secondary);
}
.row-actions {
  position: absolute;
  top: 0;
  bottom: 0;
  inset-inline-end: 0;
  display: flex;
  align-items: center;
  padding-inline: 4px;
  background: linear-gradient(var(--hover-bg), var(--hover-bg)), var(--bg-toolbar);
  opacity: 0;
}
.row:hover .row-actions,
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
.footer {
  flex-shrink: 0;
  padding: 6px 8px;
  border-top: 1px solid var(--border-color);
}
.add {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  height: 26px;
  padding: 0 8px;
  border-radius: 4px;
  font-size: 12px;
  color: var(--text-secondary);
}
.add:hover {
  color: var(--text-primary);
  background: var(--hover-bg);
}
.add span {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
