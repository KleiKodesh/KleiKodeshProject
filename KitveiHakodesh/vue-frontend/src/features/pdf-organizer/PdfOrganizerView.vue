<script setup lang="ts">
/**
 * Organize pages: a grid of page thumbnails to select, reorder (drag and drop), turn,
 * delete, copy, add blank pages or pages of another PDF, and take out as a new PDF, split
 * files or images. Save writes the document (the viewer then reloads it); Save a copy
 * writes a new file. Every change can be undone until then.
 */
import { computed, onBeforeUnmount, onMounted, ref, toRef, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { WindowedScroller, type WindowedScrollerApi } from 'vue-windowed-scroller'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import {
  IconArrowUndo20Regular,
  IconArrowRedo20Regular,
  IconSelectAllOn20Regular,
  IconSelectAllOff20Regular,
  IconArrowSwap20Regular,
  IconArrowRotateCounterclockwise20Regular,
  IconArrowRotateClockwise20Regular,
  IconCopy20Regular,
  IconDelete20Regular,
  IconDocumentOnePage20Regular,
  IconDocumentAdd20Regular,
  IconDocumentArrowRight20Regular,
  IconImage20Regular,
  IconSplitVertical20Regular,
  IconSaveCopy20Regular,
  IconSave20Regular,
} from '@iconify-prerendered/vue-fluent'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import { pickLocalFilePath, restoreLocalFile, writePdfFile } from '@/webview-host/bridge'
import { usePdfOrganizer } from './usePdfOrganizer'
import { loadPdfBytes, type OrganizerPage } from './pdfPageAssembly'
import { baseName, pdfBlob, pickOutputFolder, saveBlobAs } from './organizerFiles'
import { pageImage } from './pageImages'
import PdfOrganizerThumb from './PdfOrganizerThumb.vue'
import PdfOrganizerOptionsDialog, { type ImagesOptions, type SplitOptions } from './PdfOrganizerOptionsDialog.vue'

const props = defineProps<{ document: PDFDocumentProxy; fileName: string | null; filePath: string | null }>()
const emit = defineEmits<{ close: []; saved: [bytes: Uint8Array] }>()

const hostWindow = useHostWindow()
const organizer = usePdfOrganizer(toRef(props, 'document'))
onBeforeUnmount(() => organizer.dispose())

const { pages, selected } = organizer
const hasSelection = computed(() => selected.value.size > 0)
const busy = ref<string | null>(null)

// -- Thumbnail size (slider), remembered for the session's next organizer.
const THUMB_MIN = 90
const THUMB_MAX = 360
function readThumbWidth(): number {
  try { return Number(hostWindow.sessionStorage.getItem('pdfOrganizer.thumbWidth')) || 150 } catch { return 150 }
}
const thumbWidth = ref(readThumbWidth())
function onThumbWidth(e: Event) {
  thumbWidth.value = Number((e.target as HTMLInputElement).value)
  try { hostWindow.sessionStorage.setItem('pdfOrganizer.thumbWidth', String(thumbWidth.value)) } catch { /* private mode */ }
}

// -- The grid: a WindowedScroller of rows of `perRow` cards, so only the rows near the view are
// in the DOM (and only their pages render). perRow follows the width and the thumbnail size.
const GRID_PADDING = 16
const GRID_GAP = 12
/** A card's extra width around its thumbnail: padding and border. */
const CARD_CHROME = 20
const gridBox = ref<HTMLElement | null>(null)
const gridWidth = ref(0)
// This window's ResizeObserver (VueUse's is the main window's, late or silent for a detached window).
let gridObserver: ResizeObserver | null = null
onMounted(() => {
  const box = gridBox.value
  if (!box) return
  gridWidth.value = box.clientWidth
  gridObserver = new hostWindow.ResizeObserver(([entry]) => { if (entry) gridWidth.value = entry.contentRect.width })
  gridObserver.observe(box)
})
onBeforeUnmount(() => gridObserver?.disconnect())
const perRow = computed(() => {
  // The scroller's bar takes a little of the width; leave it room.
  const room = gridWidth.value - 2 * GRID_PADDING - 12
  return Math.max(1, Math.floor((room + GRID_GAP) / (thumbWidth.value + CARD_CHROME + GRID_GAP)))
})
const rowCount = computed(() => Math.ceil(pages.value.length / perRow.value))
function rowPages(row: number): { page: OrganizerPage; index: number }[] {
  const out: { page: OrganizerPage; index: number }[] = []
  const end = Math.min(pages.value.length, (row + 1) * perRow.value)
  for (let i = row * perRow.value; i < end; i++) out.push({ page: pages.value[i]!, index: i })
  return out
}
const gridScroller = ref<WindowedScrollerApi | null>(null)
/** A new cards-per-row is a new list: rebuilt (its start row an argument) at the row holding the first card in view. */
const gridKey = ref(0)
const gridStart = ref(0)
watch(perRow, (now, before) => {
  const first = gridScroller.value?.readFirstVisible()
  gridStart.value = first ? Math.floor((first.index * before) / now) : 0
  gridKey.value++
})

// -- Labels: the page's number in the open document, or the other file's name and page.
function pageLabel(page: OrganizerPage): string {
  if (page.pageIndex < 0) return t('pdfOrganizer.blankPage')
  const source = organizer.sources.value.get(page.source)
  return source?.bytes ? `${source.name} - ${page.pageIndex + 1}` : String(page.pageIndex + 1)
}
function blankRatio(page: OrganizerPage): number {
  const s = page.blankSize ?? { width: 612, height: 792 }
  return page.rotation % 180 ? s.height / s.width : s.width / s.height
}

// -- Click selection: plain = only this, Ctrl = toggle, Shift = range.
function onCardClick(e: MouseEvent, page: OrganizerPage) {
  organizer.select(page.key, e.shiftKey ? 'range' : e.ctrlKey || e.metaKey ? 'toggle' : 'only')
}

// -- Drag and drop: the dragged page, or all selected pages when it is one of them.
let dragKeys: Set<number> | null = null
const dropIndex = ref<number | null>(null)
function onDragStart(e: DragEvent, page: OrganizerPage) {
  if (!selected.value.has(page.key)) organizer.select(page.key, 'only')
  dragKeys = new Set(selected.value)
  e.dataTransfer?.setData('text/plain', '')
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move'
}
function onDragOver(e: DragEvent) {
  if (!dragKeys) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  const target = e.target as HTMLElement
  const card = target.closest<HTMLElement>('[data-index]')
  if (!card) {
    // Empty room in a row: after its last card; below the last row: at the end.
    const row = target.closest<HTMLElement>('[data-row]')
    if (row) dropIndex.value = Math.min(pages.value.length, (Number(row.dataset.row) + 1) * perRow.value)
    else if (target === gridScroller.value?.scrollerElement) dropIndex.value = pages.value.length
    return
  }
  const index = Number(card.dataset.index)
  const r = card.getBoundingClientRect()
  const rtl = hostWindow.getComputedStyle(card).direction === 'rtl'
  const firstHalf = rtl ? e.clientX > r.left + r.width / 2 : e.clientX < r.left + r.width / 2
  dropIndex.value = firstHalf ? index : index + 1
}
function onDrop(e: DragEvent) {
  if (!dragKeys || dropIndex.value == null) return
  e.preventDefault()
  organizer.movePages(dragKeys, dropIndex.value)
  onDragEnd()
}
function onDragEnd() {
  dragKeys = null
  dropIndex.value = null
}

// -- Output
function fail(err: unknown, key: string) {
  console.error('[PdfOrganizer]', key, err)
  showToast(t(key), { variant: 'error' })
}
async function run(label: string, work: () => Promise<void>) {
  if (busy.value) return
  busy.value = label
  try {
    await work()
  } finally {
    busy.value = null
  }
}

/** Save: the new file is written over the open one (the host's atomic whole-file write). */
async function save(): Promise<boolean> {
  if (!props.filePath) return false
  let ok = false
  await run(t('pdfOrganizer.saving'), async () => {
    try {
      const bytes = await organizer.assemble()
      const error = await writePdfFile(props.filePath!, bytes)
      if (error) throw new Error(error)
      organizer.markSaved()
      emit('saved', bytes)
      showToast(t('pdfOrganizer.saved'))
      ok = true
    } catch (err) {
      fail(err, 'pdfOrganizer.saveFailed')
    }
  })
  return ok
}
async function saveCopy(list?: OrganizerPage[], suffix = '') {
  await run(t('pdfOrganizer.saving'), async () => {
    try {
      const bytes = await organizer.assemble(list)
      await saveBlobAs(hostWindow, pdfBlob(bytes), `${baseName(props.fileName)}${suffix}.pdf`)
    } catch (err) {
      fail(err, 'pdfOrganizer.saveFailed')
    }
  })
}
function extractSelected() {
  if (!hasSelection.value) return
  void saveCopy(organizer.selectedPages.value, ` - ${t('pdfOrganizer.extractSuffix')}`)
}

const dialog = ref<'images' | 'split' | null>(null)
async function exportImages(o: ImagesOptions) {
  dialog.value = null
  const list = o.selectedOnly && hasSelection.value ? organizer.selectedPages.value : pages.value
  const write = await pickOutputFolder(hostWindow)
  if (!write) return
  await run(t('pdfOrganizer.exporting'), async () => {
    try {
      const base = baseName(props.fileName)
      const width = String(pages.value.length).length
      for (const page of list) {
        const n = pages.value.indexOf(page) + 1
        busy.value = t('pdfOrganizer.exportingPage', { page: n })
        const blob = await pageImage(hostWindow, page, organizer.sources.value, o.dpi, o.format)
        await write(`${base} - ${String(n).padStart(width, '0')}.${o.format === 'png' ? 'png' : 'jpg'}`, blob)
      }
      showToast(t('pdfOrganizer.exported', { count: list.length }))
    } catch (err) {
      fail(err, 'pdfOrganizer.exportFailed')
    }
  })
}
async function split(o: SplitOptions) {
  dialog.value = null
  const parts: OrganizerPage[][] = []
  let current: OrganizerPage[] = []
  pages.value.forEach((page, i) => {
    current.push(page)
    const cut = o.mode === 'every' ? (i + 1) % o.every === 0 : selected.value.has(page.key)
    if (cut) { parts.push(current); current = [] }
  })
  if (current.length) parts.push(current)
  if (parts.length < 2) return showToast(t('pdfOrganizer.split.nothing'))
  const write = await pickOutputFolder(hostWindow)
  if (!write) return
  await run(t('pdfOrganizer.exporting'), async () => {
    try {
      const base = baseName(props.fileName)
      let first = 1
      for (const part of parts) {
        const last = first + part.length - 1
        const bytes = await organizer.assemble(part)
        const range = first === last ? `${first}` : `${first}-${last}`
        await write(`${base} - ${range}.pdf`, pdfBlob(bytes))
        first = last + 1
      }
      showToast(t('pdfOrganizer.split.done', { count: parts.length }))
    } catch (err) {
      fail(err, 'pdfOrganizer.exportFailed')
    }
  })
}

// -- Insert pages of another PDF (the app's open-file dialog, PDF only).
async function insertFromFile() {
  const picked = await pickLocalFilePath({ pdfOnly: true })
  if (!picked) return
  await run(t('pdfOrganizer.loading'), async () => {
    try {
      const served = await restoreLocalFile(picked.filePath)
      if (!served?.url) throw new Error('file could not be opened')
      const response = await fetch(served.url)
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      const bytes = new Uint8Array(await response.arrayBuffer())
      const doc = await loadPdfBytes(bytes)
      organizer.insertDocument(doc, bytes, baseName(picked.fileName))
    } catch (err) {
      fail(err, 'pdfOrganizer.insertFailed')
    }
  })
}

// -- Closing: ask when there is something unsaved.
const confirmClose = ref(false)
function requestClose() {
  if (organizer.dirty.value) confirmClose.value = true
  else emit('close')
}
defineExpose({ requestClose })
async function saveAndClose() {
  confirmClose.value = false
  if (await save()) emit('close')
}
// The window's own close button: the browser's "leave?" question while there are changes.
useEventListener(hostWindow, 'beforeunload', (e: BeforeUnloadEvent) => {
  if (!organizer.dirty.value) return
  e.preventDefault()
  e.returnValue = ''
})

// -- Keyboard
useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (dialog.value || confirmClose.value || busy.value) return
  const target = e.target as HTMLElement | null
  if (target?.closest('input, select, textarea')) return
  const ctrl = e.ctrlKey || e.metaKey
  let handled = true
  if (ctrl && e.code === 'KeyZ' && !e.shiftKey) organizer.undo()
  else if (ctrl && (e.code === 'KeyY' || (e.code === 'KeyZ' && e.shiftKey))) organizer.redo()
  else if (ctrl && e.code === 'KeyA') organizer.selectAll()
  else if (ctrl && e.code === 'KeyS') void (props.filePath ? save() : saveCopy())
  else if (ctrl && e.code === 'KeyD') organizer.duplicateSelected()
  else if (e.key === 'Delete') organizer.deleteSelected()
  else if (e.key === 'Escape') organizer.selectNone()
  else handled = false
  if (handled) e.preventDefault()
})
</script>

<template>
  <div class="organizer" data-widget="pdf-organizer">
    <div class="org-toolbar" role="toolbar">
      <button :disabled="!organizer.canUndo.value" :title="$t('pdfOrganizer.undo')" @click="organizer.undo()"><IconArrowUndo20Regular /></button>
      <button :disabled="!organizer.canRedo.value" :title="$t('pdfOrganizer.redo')" @click="organizer.redo()"><IconArrowRedo20Regular /></button>
      <span class="sep" />
      <button :title="$t('pdfOrganizer.selectAll')" @click="organizer.selectAll()"><IconSelectAllOn20Regular /></button>
      <button :disabled="!hasSelection" :title="$t('pdfOrganizer.selectNone')" @click="organizer.selectNone()"><IconSelectAllOff20Regular /></button>
      <button :title="$t('pdfOrganizer.invertSelection')" @click="organizer.invertSelection()"><IconArrowSwap20Regular /></button>
      <span class="sep" />
      <button :disabled="!hasSelection" :title="$t('pdfOrganizer.rotateLeft')" @click="organizer.rotateSelected(-90)"><IconArrowRotateCounterclockwise20Regular /></button>
      <button :disabled="!hasSelection" :title="$t('pdfOrganizer.rotateRight')" @click="organizer.rotateSelected(90)"><IconArrowRotateClockwise20Regular /></button>
      <button :disabled="!hasSelection" :title="$t('pdfOrganizer.duplicate')" @click="organizer.duplicateSelected()"><IconCopy20Regular /></button>
      <button :disabled="!hasSelection || selected.size >= pages.length" :title="$t('pdfOrganizer.delete')" @click="organizer.deleteSelected()"><IconDelete20Regular /></button>
      <span class="sep" />
      <button :title="$t('pdfOrganizer.insertBlank')" @click="organizer.insertBlank()"><IconDocumentOnePage20Regular /></button>
      <button :title="$t('pdfOrganizer.insertFile')" @click="insertFromFile"><IconDocumentAdd20Regular /></button>
      <span class="sep" />
      <button :disabled="!hasSelection" :title="$t('pdfOrganizer.extract')" @click="extractSelected"><IconDocumentArrowRight20Regular /></button>
      <button :title="$t('pdfOrganizer.images.title')" @click="dialog = 'images'"><IconImage20Regular /></button>
      <button :disabled="pages.length < 2" :title="$t('pdfOrganizer.split.title')" @click="dialog = 'split'"><IconSplitVertical20Regular /></button>
      <span class="spacer" />
      <input
        class="size-slider"
        type="range"
        :min="THUMB_MIN"
        :max="THUMB_MAX"
        step="10"
        :value="thumbWidth"
        :title="$t('pdfOrganizer.thumbSize')"
        :aria-label="$t('pdfOrganizer.thumbSize')"
        @input="onThumbWidth"
      />
      <span class="sep" />
      <button :title="$t('pdfOrganizer.saveCopy')" @click="saveCopy()"><IconSaveCopy20Regular /></button>
      <button
        class="save-btn"
        :disabled="!filePath || !organizer.dirty.value"
        :title="filePath ? $t('pdfOrganizer.save') : $t('pdfOrganizer.saveNoFile')"
        @click="save"
      >
        <IconSave20Regular />
        <span>{{ $t('pdfOrganizer.saveShort') }}</span>
      </button>
    </div>

    <div ref="gridBox" class="org-grid-box">
      <WindowedScroller
        v-if="gridWidth > 0"
        :key="gridKey"
        ref="gridScroller"
        class="org-grid"
        :class="{ 'has-selection': hasSelection }"
        thin
        :count="rowCount"
        :initial-index="Math.min(gridStart, Math.max(0, rowCount - 1))"
        :row-source-version="pages"
        :end-padding-em="1"
        :style="{ '--thumb-w': `${thumbWidth}px`, '--per-row': perRow }"
        @click.self="organizer.selectNone()"
        @dragover="onDragOver"
        @drop="onDrop"
      >
        <template #row="{ index: row }">
          <div class="org-row" :class="{ first: row === 0 }" :data-row="row" @click.self="organizer.selectNone()">
            <div
              v-for="{ page, index: i } in rowPages(row)"
              :key="page.key"
              class="card"
              :class="{
                selected: selected.has(page.key),
                'drop-before': dropIndex === i,
                'drop-after': dropIndex === pages.length && i === pages.length - 1,
              }"
              :data-index="i"
              draggable="true"
              :title="pageLabel(page)"
              @click="onCardClick($event, page)"
              @dragstart="onDragStart($event, page)"
              @dragend="onDragEnd"
            >
              <input
                class="card-check"
                type="checkbox"
                :checked="selected.has(page.key)"
                :aria-label="$t('pdfOrganizer.selectPage', { page: i + 1 })"
                @click.stop="organizer.select(page.key, $event.shiftKey ? 'range' : 'toggle')"
              />
              <PdfOrganizerThumb
                :doc="organizer.sources.value.get(page.source)?.doc ?? null"
                :page-index="page.pageIndex"
                :rotation="page.rotation"
                :width="thumbWidth"
                :blank-ratio="blankRatio(page)"
              />
              <div class="card-label">
                <span class="card-pos">{{ i + 1 }}</span>
                <span v-if="page.pageIndex < 0 || organizer.sources.value.get(page.source)?.bytes || page.pageIndex !== i" class="card-origin">{{ pageLabel(page) }}</span>
              </div>
            </div>
          </div>
        </template>
      </WindowedScroller>
    </div>

    <div class="org-status">
      <span>{{ $t('pdfOrganizer.status', { pages: pages.length, selected: selected.size }) }}</span>
      <span v-if="organizer.dirty.value" class="unsaved">{{ $t('pdfOrganizer.unsaved') }}</span>
    </div>

    <div v-if="busy" class="org-busy">
      <LoadingAnimation />
      <span>{{ busy }}</span>
    </div>

    <PdfOrganizerOptionsDialog
      v-if="dialog"
      :kind="dialog"
      :has-selection="hasSelection"
      @cancel="dialog = null"
      @images="exportImages"
      @split="split"
    />
    <ConfirmDialog
      v-if="confirmClose"
      :title="$t('pdfOrganizer.unsavedTitle')"
      :desc="$t('pdfOrganizer.unsavedDesc')"
      :confirm-label="$t('pdfOrganizer.closeWithoutSaving')"
      :extra-label="filePath ? $t('pdfOrganizer.saveShort') : undefined"
      @confirm="confirmClose = false; emit('close')"
      @cancel="confirmClose = false"
      @extra="saveAndClose"
    />
  </div>
</template>

<style scoped>
.organizer {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  background: var(--bg-primary);
  color: var(--text-primary);
}
.org-toolbar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 4px 8px;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-wrap: wrap;
}
.org-toolbar button {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-width: var(--toolbar-button-size, 32px);
  height: var(--toolbar-button-size, 32px);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
.org-toolbar button svg {
  width: 16px;
  height: 16px;
}
.org-toolbar button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.org-toolbar .save-btn {
  padding-inline: 10px;
  color: var(--accent-color);
}
.sep {
  width: 1px;
  height: 20px;
  margin: 0 4px;
  background: var(--border-color);
}
.spacer {
  flex: 1;
}
.size-slider {
  width: 110px;
  accent-color: var(--accent-color);
}
.org-grid-box {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  background: var(--bg-secondary);
}
.org-grid-box > .windowed-scroller {
  flex: 1;
  min-height: 0;
}
.org-row {
  display: grid;
  grid-template-columns: repeat(var(--per-row), minmax(0, 1fr));
  gap: 12px;
  padding: 6px 16px;
}
.org-row.first {
  padding-top: 16px;
}
.card {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 8px;
  border: 2px solid transparent;
  border-radius: 6px;
  cursor: pointer;
  user-select: none;
}
.card:hover {
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
}
.card.selected {
  border-color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 14%, transparent);
}
/* The insertion marker: a bar at the card's leading (or trailing) edge. */
.card.drop-before::before,
.card.drop-after::after {
  content: '';
  position: absolute;
  top: 4px;
  bottom: 4px;
  width: 3px;
  border-radius: 2px;
  background: var(--accent-color);
}
.card.drop-before::before {
  inset-inline-start: -9px;
}
.card.drop-after::after {
  inset-inline-end: -9px;
}
.card-check {
  position: absolute;
  top: 10px;
  inset-inline-start: 10px;
  z-index: 1;
  width: 16px;
  height: 16px;
  margin: 0;
  accent-color: var(--accent-color);
  opacity: 0;
}
.card:hover .card-check,
.card.selected .card-check,
.org-grid.has-selection .card-check {
  opacity: 1;
}
.card-label {
  display: flex;
  flex-direction: column;
  align-items: center;
  font-size: 12px;
  max-width: 100%;
}
.card-pos {
  font-weight: 600;
}
.card-origin {
  color: var(--text-secondary);
  font-size: 11px;
  max-width: 100%;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.org-status {
  display: flex;
  justify-content: space-between;
  padding: 4px 12px;
  font-size: 12px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-toolbar);
}
.unsaved {
  color: var(--accent-color);
}
.org-busy {
  position: absolute;
  inset: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  background: color-mix(in srgb, var(--bg-primary) 70%, transparent);
}
</style>
