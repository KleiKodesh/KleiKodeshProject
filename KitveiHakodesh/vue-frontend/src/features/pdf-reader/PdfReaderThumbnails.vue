<script setup lang="ts">
import { computed, ref, shallowReactive, watch } from 'vue'
import {
  IconChevronDown12Regular,
  IconClipboardPaste16Regular,
  IconCopy16Regular,
  IconCut16Regular,
  IconDelete16Regular,
  IconDismiss16Regular,
  IconDocumentArrowRight16Regular,
} from '@iconify-prerendered/vue-fluent'
import { useI18n } from 'vue-i18n'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import type { PdfPageEdits } from './usePdfPageEdits'
import { WindowedScroller, type WindowedScrollerApi } from 'vue-windowed-scroller'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import PdfThumbImage from './PdfThumbImage.vue'

/**
 * Page thumbnails: one WindowedScroller row per page, so only the rows near the view are in
 * the DOM and only they ask the shared thumbnail engine (pdfThumbnailEngine) for a picture.
 * Opens at the current page (its start row), marks it and keeps it in view; click or Enter
 * goes there, Up / Down / Home / End move.
 * With `edits`: the old viewer's page selection - a checkbox on each page, select all, the
 * "Manage" menu (copy, cut, delete, export selected), paste buttons and the undo bar
 * (usePdfPageEdits); Ctrl+C / Ctrl+X / Delete act on the selected pages.
 */
const props = defineProps<{
  document: PDFDocumentProxy
  pageNumber: number
  rotation: number
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
  /** Page selection and the Manage menu; without it the panel only shows the pages. */
  edits?: PdfPageEdits
}>()
const emit = defineEmits<{ select: [pageNumber: number] }>()

const THUMB_WIDTH = 120

/** A new document is a new list (re-keyed: its start row is read once). */
const docIds = new WeakMap<PDFDocumentProxy, number>()
let nextDocId = 1
function docKey(doc: PDFDocumentProxy): number {
  let id = docIds.get(doc)
  if (!id) docIds.set(doc, (id = nextDocId++))
  return id
}

const scroller = ref<WindowedScrollerApi | null>(null)
/** The panel opens at the page being read (read once: the scroller's start row). */
const initialIndex = props.pageNumber - 1
/** Width / height of pages already drawn, at the current rotation; others take the first page's. */
const aspects = shallowReactive(new Map<number, number>())
const defaultAspect = ref(1 / Math.SQRT2)

watch(
  () => [props.document, props.rotation] as const,
  ([doc, rotation]) => {
    aspects.clear()
    void doc.getPage(1).then((p) => {
      if (doc !== props.document || rotation !== props.rotation) return
      const v = p.getViewport({ scale: 1, rotation: (p.rotate + rotation) % 360 })
      defaultAspect.value = v.width / v.height
    }).catch(() => {})
  },
  { immediate: true },
)

// The current page follows the reader: brought into view (nearest edge) by the scroller itself.
watch(() => props.pageNumber, (n) => scroller.value?.revealIndex(n - 1))

function onKeydown(e: KeyboardEvent) {
  // The old viewer's keys on the selected pages (PDFThumbnailViewer): Ctrl+C, Ctrl+X, Delete / Backspace.
  const edits = props.edits
  const ctrl = e.ctrlKey || e.metaKey
  if (edits && edits.size.value && !e.altKey && !e.shiftKey) {
    const act = ctrl && e.code === 'KeyC' ? edits.copy
      : ctrl && e.code === 'KeyX' ? edits.cut
        : !ctrl && (e.key === 'Delete' || e.key === 'Backspace') && !edits.pasteMode.value ? edits.remove
          : null
    if (act) {
      e.preventDefault()
      e.stopPropagation()
      void act()
      return
    }
  }
  const step = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : e.key === 'Home' ? -Infinity : e.key === 'End' ? Infinity : 0
  if (step) {
    e.preventDefault()
    emit('select', Math.min(props.document.numPages, Math.max(1, props.pageNumber + step)))
  } else if (e.key === 'Enter') {
    e.preventDefault()
    emit('select', props.pageNumber)
  }
}

// -- Selection status, the Manage menu and the undo bar (the old viewer's views manager header).
const { t } = useI18n()
const statusLabel = computed(() => {
  const n = props.edits?.size.value ?? 0
  return n === 0 ? t('pdfReader.pageManage.selectPages') : n === 1 ? t('pdfReader.pageManage.selectedOne') : t('pdfReader.pageManage.selectedMany', { count: n })
})
const undoLabel = computed(() => {
  const bar = props.edits?.undoBar.value
  if (!bar) return ''
  return t(`pdfReader.pageManage.${bar.kind}${bar.count === 1 ? 'One' : 'Many'}`, { count: bar.count })
})
const manageMenu = ref<InstanceType<typeof ContextMenu> | null>(null)
const manageItems = computed<ContextMenuItem[]>(() => {
  const e = props.edits
  if (!e) return []
  const none = !e.size.value
  return [
    { label: t('pdfReader.pageManage.copy'), icon: IconCopy16Regular, shortcut: 'Ctrl+C', disabled: none, action: () => void e.copy() },
    { label: t('pdfReader.pageManage.cut'), icon: IconCut16Regular, shortcut: 'Ctrl+X', disabled: !e.canDelete.value, action: () => void e.cut() },
    { label: t('pdfReader.pageManage.delete'), icon: IconDelete16Regular, shortcut: 'Delete', disabled: !e.canDelete.value, action: () => void e.remove() },
    { label: t('pdfReader.pageManage.exportSelected'), icon: IconDocumentArrowRight16Regular, disabled: none, action: () => void e.exportSelected() },
  ]
})
function openManage(event: MouseEvent) {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  void manageMenu.value?.showAtPosition(rect.left, rect.bottom)
}

function onOpened() {
  scroller.value?.scrollerElement?.focus({ preventScroll: true })
}
</script>

<template>
  <div class="thumbs-panel">
  <div v-if="edits" class="thumbs-status">
    <template v-if="edits.undoBar.value">
      <span class="status-label">{{ undoLabel }}</span>
      <button type="button" class="status-button" :disabled="edits.busy.value" @click="edits.undo()">{{ $t('pdfReader.pageManage.undo') }}</button>
      <button type="button" class="status-icon" :title="$t('pdfReader.pageManage.close')" @click="edits.dismissUndo()"><IconDismiss16Regular /></button>
    </template>
    <template v-else>
      <input
        type="checkbox"
        class="select-all"
        :title="$t('pdfReader.pageManage.selectAll')"
        :checked="edits.allSelected.value"
        :indeterminate="edits.someSelected.value"
        :disabled="!edits.pageCount.value"
        @change="edits.toggleSelectAll(($event.target as HTMLInputElement).checked)"
      >
      <span class="status-label">{{ statusLabel }}</span>
      <button type="button" class="status-button manage" :disabled="edits.busy.value" @mousedown.prevent @click="openManage">
        <span>{{ $t('pdfReader.pageManage.manage') }}</span>
        <IconChevronDown12Regular />
      </button>
    </template>
    <ContextMenu ref="manageMenu" :items="manageItems" />
  </div>
  <WindowedScroller
    :key="docKey(document)"
    ref="scroller"
    class="thumbs"
    thin
    tabindex="0"
    :data-ctrlx-enabled="edits?.size.value ? '' : undefined"
    :count="document.numPages"
    :initial-index="initialIndex"
    :end-padding-em="0.6"
    @keydown="onKeydown"
    @opened="onOpened"
  >
    <template #row="{ index }">
      <div class="thumb-row" :class="{ first: index === 0 }">
        <button
          v-if="edits?.pasteMode.value && index === 0"
          type="button"
          class="paste-button"
          :title="$t('pdfReader.pageManage.pasteBefore')"
          :disabled="edits.busy.value"
          @click="edits.paste(0)"
        >
          <IconClipboardPaste16Regular /><span>{{ $t('pdfReader.pageManage.paste') }}</span>
        </button>
        <div class="thumb-cell">
        <input
          v-if="edits"
          type="checkbox"
          class="thumb-check"
          tabindex="-1"
          :title="$t('pdfReader.pageManage.selectPage', { n: labelOf?.(index + 1) ?? index + 1 })"
          :checked="edits.selected.value.has(index + 1)"
          @change="edits.selectPage(index + 1, ($event.target as HTMLInputElement).checked)"
        >
        <button
          type="button"
          class="thumb"
          :class="{ current: index + 1 === pageNumber, selected: edits?.selected.value.has(index + 1) }"
          :title="$t('pdfReader.panel.page', { n: labelOf?.(index + 1) ?? index + 1 })"
          tabindex="-1"
          @click="emit('select', index + 1)"
        >
          <span class="thumb-frame" :style="{ width: `${THUMB_WIDTH}px`, aspectRatio: String(aspects.get(index) ?? defaultAspect) }">
            <PdfThumbImage :doc="document" :page-index="index" :rotation="rotation" :css-width="THUMB_WIDTH" @aspect="aspects.set(index, $event)" />
          </span>
          <span class="thumb-label">{{ labelOf?.(index + 1) ?? index + 1 }}</span>
        </button>
        </div>
        <button
          v-if="edits?.pasteMode.value"
          type="button"
          class="paste-button"
          :title="$t('pdfReader.pageManage.pasteAfter', { page: labelOf?.(index + 1) ?? index + 1 })"
          :disabled="edits.busy.value"
          @click="edits.paste(index + 1)"
        >
          <IconClipboardPaste16Regular /><span>{{ $t('pdfReader.pageManage.paste') }}</span>
        </button>
      </div>
    </template>
  </WindowedScroller>
  </div>
</template>

<style scoped>
.thumbs-panel {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.thumbs-panel > .windowed-scroller {
  flex: 1;
  min-height: 0;
}
.thumbs {
  outline: none;
}
.thumb-row {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
}
.thumb-cell {
  position: relative;
}
/* The page's checkbox, on its top corner (inline-start), as the old viewer's. */
.thumb-check {
  position: absolute;
  top: 8px;
  inset-inline-start: 8px;
  z-index: 1;
  margin: 0;
  accent-color: var(--accent-color);
  cursor: pointer;
}
.thumb.selected .thumb-frame {
  box-shadow: 0 0 0 2px var(--accent-color);
}
.paste-button {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 24px;
  padding: 0 10px;
  border-radius: 12px;
  border: 1px dashed var(--accent-color);
  color: var(--accent-color);
  background: none;
  font-size: 12px;
}
.paste-button:hover {
  background: var(--hover-bg);
}
.thumbs-status {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 32px;
  padding: 2px 8px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  font-size: 12px;
}
.select-all {
  margin: 0;
  accent-color: var(--accent-color);
  cursor: pointer;
}
.status-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  color: var(--text-secondary);
}
.status-button {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 24px;
  padding: 0 8px;
  border-radius: 4px;
  background: none;
  font-size: 12px;
}
.status-button:hover,
.status-icon:hover {
  background: var(--hover-bg);
}
.status-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 4px;
  background: none;
}
.thumb-row.first {
  padding-top: 10px;
}
.thumb {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  height: auto;
  padding: 4px;
  border-radius: 4px;
  background: none;
}
.thumb:hover {
  background: var(--hover-bg);
}
.thumb-frame {
  display: block;
  background: #fff;
  box-shadow: 0 0 0 1px var(--border-color);
  overflow: hidden;
}
.thumb.current .thumb-frame {
  box-shadow: 0 0 0 2px var(--accent-color);
}
.thumbs:focus-visible .thumb.current {
  background: var(--hover-bg);
}
.thumb-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.thumb.current .thumb-label {
  color: var(--accent-color);
}
</style>
