<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { useElementSize } from '@vueuse/core'
import {
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconAutoFitWidth20Regular,
  IconScaleFit20Regular,
  IconRatioOneToOne20Regular,
  IconPageFit20Regular,
  IconSearch20Regular,
  IconEdit20Regular,
  IconEdit20Filled,
  IconScanText20Regular,
  IconMoreHorizontal24Regular,
  IconGrid20Regular,
  IconPrint20Regular,
  IconSave20Regular,
  IconSaveEdit20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { usePressRepeat } from '@/composables/usePressRepeat'
import { useToolbarDrag } from '@/composables/useToolbarDrag'
import { useBookViewStore } from '@/stores/bookViewStore'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import IconPdfScrollPage from './icons/IconPdfScrollPage.vue'
import IconPdfScrollVertical from './icons/IconPdfScrollVertical.vue'
import IconPdfScrollHorizontal from './icons/IconPdfScrollHorizontal.vue'
import IconPdfScrollWrapped from './icons/IconPdfScrollWrapped.vue'
import IconPdfSpreadNone from './icons/IconPdfSpreadNone.vue'
import IconPdfSpreadOdd from './icons/IconPdfSpreadOdd.vue'
import IconPdfSpreadEven from './icons/IconPdfSpreadEven.vue'
import IconPdfFirstPage from './icons/IconPdfFirstPage.vue'
import IconPdfLastPage from './icons/IconPdfLastPage.vue'
import IconPdfRotateClockwise from './icons/IconPdfRotateClockwise.vue'
import IconPdfRotateCounterclockwise from './icons/IconPdfRotateCounterclockwise.vue'
import IconPdfPresentation from './icons/IconPdfPresentation.vue'
import IconPdfDocumentProperties from './icons/IconPdfDocumentProperties.vue'
import IconPdfSelectTool from './icons/IconPdfSelectTool.vue'
import IconPdfHandTool from './icons/IconPdfHandTool.vue'
import ContextMenu, { type ContextMenuItem, type ContextMenuLeafItem } from '@/components/ContextMenu.vue'
import { useToolbarOverflow, TOOLBAR_BUTTON_SIZE } from '@/composables/useToolbarOverflow'
import PdfIconDropdown, { type IconDropdownOption } from './PdfIconDropdown.vue'
import { PDF_KEYS as K, withKey } from './usePdfReaderShortcuts'
import { MAX_SCALE, MIN_SCALE, ScrollMode, SpreadMode, type ScalePreset } from './usePdfScroller'

const props = defineProps<{
  pageNumber: number
  pageCount: number
  /** The current page's label when the document has page labels (shown with "(n of N)"), else null. */
  pageLabel?: string | null
  scale: number
  preset: ScalePreset | null
  scrollMode: number
  spreadMode: number
  searchVisible: boolean
  handTool: boolean
  editVisible: boolean
  ocrActive: boolean
  tocVisible: boolean
  hasOutline: boolean
  organizerOpen?: boolean
  /** Save is possible: a file to save into and changes not in it yet. */
  canSave?: boolean
  /** The document has changes not saved in its file (the dot on the Save button). */
  dirty?: boolean
}>()

const emit = defineEmits<{
  goToPage: [page: number]
  /** A typed page: a page label or a number (usePdfScroller goToPageLabel). */
  goToPageText: [text: string]
  /** First / last page: a jump, remembered for Back (pdfJumpHistory). */
  jumpToPage: [page: number]
  zoomIn: []
  zoomOut: []
  setZoom: [percent: number]
  setPreset: [preset: ScalePreset]
  setScrollMode: [mode: number]
  setSpreadMode: [mode: number]
  rotate: [delta: 90 | -90]
  toggleSearch: []
  toggleHandTool: []
  toggleEdit: []
  toggleOcr: []
  toggleToc: [toggleButton: HTMLElement]
  presentation: []
  properties: []
  print: []
  toggleOrganizer: []
  save: []
  saveAs: []
}>()

const hostWindow = useHostWindow()
const { t } = useI18n()
const zoomRepeat = usePressRepeat()

// The book view's toolbar shell and position (shared useToolbarDrag + app-wide toolbar position).
const { toolbarPosition } = storeToRefs(useBookViewStore())
const toolbarEl = ref<HTMLElement | null>(null)
const { isDragging, dropPreviewStyle, isOverBackground } = useToolbarDrag(toolbarEl, toolbarPosition, '.pdf-reader')

// Dropdowns: the button shows the chosen item's icon, its name in the tooltip.
const presetOptions = computed<IconDropdownOption<ScalePreset>[]>(() => [
  { value: 'page-width', label: t('pdfReader.fitWidth'), icon: IconAutoFitWidth20Regular },
  { value: 'auto', label: t('pdfReader.fitAuto'), icon: IconScaleFit20Regular },
  { value: 'page-actual', label: t('pdfReader.fitActual'), icon: IconRatioOneToOne20Regular },
  { value: 'page-fit', label: t('pdfReader.fitPage'), icon: IconPageFit20Regular },
])
const scrollOptions = computed<IconDropdownOption<number>[]>(() => [
  { value: ScrollMode.VERTICAL, label: t('pdfReader.scrollVertical'), icon: IconPdfScrollVertical },
  { value: ScrollMode.PAGE, label: t('pdfReader.scrollPage'), icon: IconPdfScrollPage },
  { value: ScrollMode.HORIZONTAL, label: t('pdfReader.scrollHorizontal'), icon: IconPdfScrollHorizontal },
  { value: ScrollMode.WRAPPED, label: t('pdfReader.scrollWrapped'), icon: IconPdfScrollWrapped },
])
const spreadOptions = computed<IconDropdownOption<number>[]>(() => [
  { value: SpreadMode.NONE, label: t('pdfReader.spreadNone'), icon: IconPdfSpreadNone },
  { value: SpreadMode.ODD, label: t('pdfReader.spreadOdd'), icon: IconPdfSpreadOdd },
  { value: SpreadMode.EVEN, label: t('pdfReader.spreadEven'), icon: IconPdfSpreadEven },
])

// -- Overflow: a narrow toolbar collapses controls into the "more" menu (the book view's
// shared fit arithmetic, useToolbarOverflow). First key = first to go; reading essentials last.
const OVERFLOW_ORDER = [
  'properties', 'print', 'organize', 'presentation', 'rotate', 'spread', 'scroll', 'preset',
  'saveas', 'ocr', 'edit', 'hand', 'zoom', 'first-last', 'search', 'save', 'toc', 'pages',
] as const
type OverflowKey = (typeof OVERFLOW_ORDER)[number]
/** The controls in the order the toolbar shows them. */
const TOOLBAR_ORDER: OverflowKey[] = [
  'toc', 'first-last', 'pages', 'search', 'hand', 'edit', 'ocr',
  'zoom', 'preset', 'scroll', 'spread', 'rotate', 'presentation', 'organize', 'save', 'saveas', 'print', 'properties',
]
const OVERFLOW_GROUP: Record<OverflowKey, string> = {
  properties: 'document', print: 'document', organize: 'document', presentation: 'document',
  save: 'document', saveas: 'document',
  rotate: 'layout', spread: 'layout', scroll: 'layout', preset: 'layout',
  ocr: 'tools', edit: 'tools', hand: 'tools',
  zoom: 'zoom',
  'first-last': 'pages', pages: 'pages',
  search: 'panels', toc: 'panels',
}
// The page box ("1 / 364") and zoom box are measured, not guessed: a guess too wide collapses
// controls while there is still room. The last width is kept while a box sits in the menu.
const pageBoxEl = ref<HTMLElement | null>(null)
const zoomBoxEl = ref<HTMLElement | null>(null)
const { width: pageBoxW } = useElementSize(pageBoxEl, undefined, { box: 'border-box' })
const { width: zoomBoxW } = useElementSize(zoomBoxEl, undefined, { box: 'border-box' })
let lastPageBoxW = 78
let lastZoomBoxW = 46
/** Room each control takes, in button-widths. */
function widthOf(key: OverflowKey): number {
  if (key === 'rotate' || key === 'first-last') return 2
  if (key === 'zoom') {
    if (zoomBoxW.value > 0) lastZoomBoxW = zoomBoxW.value
    return 2 + lastZoomBoxW / TOOLBAR_BUTTON_SIZE
  }
  if (key === 'pages') {
    if (pageBoxW.value > 0) lastPageBoxW = pageBoxW.value
    return 2 + lastPageBoxW / TOOLBAR_BUTTON_SIZE
  }
  return 1
}
const isVertical = computed(() => toolbarPosition.value === 'left' || toolbarPosition.value === 'right')
const { hasToolbarOverflow, overflowedKeys, toolbarButtonVisible: shown } = useToolbarOverflow(
  toolbarEl,
  computed(() => [...OVERFLOW_ORDER]),
  isVertical,
  widthOf,
)
const moreButton = ref<HTMLElement | null>(null)
const moreMenu = ref<InstanceType<typeof ContextMenu> | null>(null)
function openMore() {
  const r = moreButton.value?.getBoundingClientRect()
  if (r) void moreMenu.value?.showAtPosition(r.left, r.bottom)
}
function radio<V>(options: IconDropdownOption<V>[], value: V | null, pick: (v: V) => void): ContextMenuLeafItem[] {
  return options.map((o) => ({ type: 'checkbox' as const, label: o.label, icon: o.icon, checked: o.value === value, onChange: () => { pick(o.value); moreMenu.value?.hide() } }))
}
/** One face per collapsed control, in collapse order; each runs the same emit as its toolbar button. */
const moreItems = computed<ContextMenuItem[]>(() => {
  const items: ContextMenuItem[] = []
  let lastGroup = ''
  // in the toolbar's own order (the collapse order only decides WHAT goes)
  const keys = [...overflowedKeys.value].sort((x, y) => TOOLBAR_ORDER.indexOf(x) - TOOLBAR_ORDER.indexOf(y))
  for (const key of keys) {
    // a separator between the toolbar's groups (document, layout, tools, zoom, pages, panels)
    const group = OVERFLOW_GROUP[key]
    if (items.length && group !== lastGroup) items.push({ type: 'separator' })
    lastGroup = group
    if (key === 'properties') items.push({ icon: IconPdfDocumentProperties, label: t('pdfReader.properties'), shortcut: K.properties, action: () => emit('properties') })
    else if (key === 'save') {
      if (props.canSave) items.push({ icon: IconSave20Regular, label: t('pdfReader.saveDoc.save'), shortcut: K.save, action: () => emit('save') })
    } else if (key === 'saveas') items.push({ icon: IconSaveEdit20Regular, label: t('pdfReader.saveDoc.saveAs'), shortcut: K.saveAs, action: () => emit('saveAs') })
    else if (key === 'print') items.push({ icon: IconPrint20Regular, label: t('pdfReader.print'), shortcut: K.print, action: () => emit('print') })
    else if (key === 'organize') items.push({ type: 'checkbox', label: withKey(t('pdfOrganizer.toggle'), K.organizer), icon: IconGrid20Regular, checked: !!props.organizerOpen, onChange: () => { emit('toggleOrganizer'); moreMenu.value?.hide() } })
    else if (key === 'presentation') items.push({ icon: IconPdfPresentation, label: t('pdfReader.presentation'), shortcut: K.presentation, action: () => emit('presentation') })
    else if (key === 'rotate') {
      items.push({ icon: IconPdfRotateCounterclockwise, label: t('pdfReader.rotateLeft'), shortcut: K.rotateLeft, action: () => emit('rotate', -90) })
      items.push({ icon: IconPdfRotateClockwise, label: t('pdfReader.rotateRight'), shortcut: K.rotateRight, action: () => emit('rotate', 90) })
    } else if (key === 'spread') items.push({ type: 'submenu', icon: spreadOptions.value.find((o) => o.value === props.spreadMode)?.icon, label: withKey(t('pdfReader.menuSpread'), K.spreadCycle), items: radio(spreadOptions.value, props.spreadMode, (v) => emit('setSpreadMode', v)) })
    else if (key === 'scroll') items.push({ type: 'submenu', icon: scrollOptions.value.find((o) => o.value === props.scrollMode)?.icon, label: withKey(t('pdfReader.menuScroll'), K.scrollCycle), items: radio(scrollOptions.value, props.scrollMode, (v) => emit('setScrollMode', v)) })
    else if (key === 'preset') items.push({ type: 'submenu', icon: (presetOptions.value.find((o) => o.value === props.preset) ?? presetOptions.value[0])?.icon, label: withKey(t('pdfReader.menuFit'), K.fitCycle), items: radio(presetOptions.value, props.preset, (v) => emit('setPreset', v)) })
    else if (key === 'ocr') items.push({ type: 'checkbox', label: withKey(t('pdfReader.ocr'), K.ocr), icon: IconScanText20Regular, checked: props.ocrActive, onChange: () => { emit('toggleOcr'); moreMenu.value?.hide() } })
    else if (key === 'edit') items.push({ type: 'checkbox', label: withKey(t('pdfReader.toggleEdit'), K.edit), icon: IconEdit20Regular, checked: props.editVisible, onChange: () => { emit('toggleEdit'); moreMenu.value?.hide() } })
    else if (key === 'hand') items.push({ type: 'checkbox', label: withKey(t('pdfReader.handTool'), K.handTool), icon: IconPdfHandTool, checked: props.handTool, onChange: () => { emit('toggleHandTool'); moreMenu.value?.hide() } })
    else if (key === 'zoom') {
      items.push({ icon: IconZoomIn20Regular, label: t('pdfReader.zoomIn', { zoom: Math.round(props.scale * 100) }), shortcut: K.zoomIn, action: () => emit('zoomIn') })
      items.push({ icon: IconZoomOut20Regular, label: t('pdfReader.zoomOut', { zoom: Math.round(props.scale * 100) }), shortcut: K.zoomOut, action: () => emit('zoomOut') })
    } else if (key === 'first-last') {
      items.push({ icon: IconPdfFirstPage, label: t('pdfReader.firstPage'), shortcut: K.firstPage, action: () => emit('jumpToPage', 1) })
      items.push({ icon: IconPdfLastPage, label: t('pdfReader.lastPage'), shortcut: K.lastPage, action: () => emit('jumpToPage', props.pageCount) })
    } else if (key === 'search') items.push({ type: 'checkbox', label: withKey(t('pdfReader.search'), K.search), icon: IconSearch20Regular, checked: props.searchVisible, onChange: () => { emit('toggleSearch'); moreMenu.value?.hide() } })
    else if (key === 'toc') {
      if (props.hasOutline) items.push({ type: 'checkbox', label: withKey(t('pdfReader.toc'), K.toc), icon: IconTreeRtl, checked: props.tocVisible, onChange: () => { if (moreButton.value) emit('toggleToc', moreButton.value); moreMenu.value?.hide() } })
    } else if (key === 'pages') {
      items.push({ icon: IconChevronRight20Regular, label: t('pdfReader.prevPage'), shortcut: K.prevPage, action: () => emit('goToPage', props.pageNumber - 1) })
      items.push({ icon: IconChevronLeft20Regular, label: t('pdfReader.nextPage'), shortcut: K.nextPage, action: () => emit('goToPage', props.pageNumber + 1) })
    }
  }
  if (items[items.length - 1]?.type === 'separator') items.pop()
  return items
})

// Page box: typing edits a draft; Enter jumps, Escape discards the draft.
// With page labels (as PDF.js): the box shows the label and accepts a label or a page number.
const pageShown = () => props.pageLabel || String(props.pageNumber)
const pageDraft = ref(pageShown())
watch(() => [props.pageNumber, props.pageLabel], () => (pageDraft.value = pageShown()))
function commitPage() {
  const typed = pageDraft.value.trim()
  if (typed && typed !== pageShown()) emit('goToPageText', typed)
  pageDraft.value = pageShown()
}
function discardPage(event: KeyboardEvent) {
  pageDraft.value = pageShown()
  ;(event.target as HTMLInputElement).blur()
}
// Zoom box: type a percentage; Enter applies, Escape discards the typed text and applies nothing.
const zoomDraft = ref(String(Math.round(props.scale * 100)))
watch(() => props.scale, (s) => (zoomDraft.value = String(Math.round(s * 100))))
function commitZoom() {
  const n = Number.parseFloat(zoomDraft.value)
  if (Number.isFinite(n) && n > 0) emit('setZoom', n)
  else zoomDraft.value = String(Math.round(props.scale * 100))
}
function discardZoom(event: KeyboardEvent) {
  zoomDraft.value = String(Math.round(props.scale * 100))
  ;(event.target as HTMLInputElement).blur()
}
</script>

<template>
  <div
    ref="toolbarEl"
    class="pdf-reader-toolbar reading-toolbar"
    dir="rtl"
    :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging, 'has-overflow': hasToolbarOverflow }]"
    :title="isOverBackground ? $t('bookView.toolbar.dragHint') : undefined"
  >
    <Teleport :to="hostWindow.document.body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>


    <!-- table of contents (the PDF's outline), as the book view / FastDocView TOC button -->
    <template v-if="shown('toc')">
    <button
      :class="{ active: tocVisible }"
      :disabled="!hasOutline"
      :title="withKey($t('pdfReader.toc'), K.toc)"
      @click="emit('toggleToc', $event.currentTarget as HTMLElement)"
    >
      <IconTreeRtl />
    </button>
    </template>
    <!-- pages -->
    <template v-if="shown('first-last')">
    <button :disabled="pageNumber <= 1" :title="withKey($t('pdfReader.firstPage'), K.firstPage)" @click="emit('jumpToPage', 1)">
      <IconPdfFirstPage />
    </button>
    </template>
    <template v-if="shown('pages')">
    <button :disabled="pageNumber <= 1" :title="withKey($t('pdfReader.prevPage'), K.prevPage)" @click="emit('goToPage', pageNumber - 1)">
      <IconChevronRight20Regular class="dir-icon" />
    </button>
    <span ref="pageBoxEl" class="page-box">
      <input
        v-model="pageDraft"
        class="page-input"
        :class="{ labeled: !!pageLabel }"
        :inputmode="pageLabel ? 'text' : 'numeric'"
        :aria-label="$t('pdfReader.pageNumber')"
        :title="withKey($t('pdfReader.pageNumber'), K.goToPage)"
        @keydown.enter="commitPage"
        @keydown.esc.stop="discardPage"
        @blur="commitPage"
      />
      <span v-if="pageLabel" class="page-count">{{ $t('pdfReader.pageOfCount', { page: pageNumber, count: pageCount }) }}</span>
      <span v-else class="page-count">/ {{ pageCount }}</span>
    </span>
    <button :disabled="pageNumber >= pageCount" :title="withKey($t('pdfReader.nextPage'), K.nextPage)" @click="emit('goToPage', pageNumber + 1)">
      <IconChevronLeft20Regular class="dir-icon" />
    </button>
    </template>
    <template v-if="shown('first-last')">
    <button :disabled="pageNumber >= pageCount" :title="withKey($t('pdfReader.lastPage'), K.lastPage)" @click="emit('jumpToPage', pageCount)">
      <IconPdfLastPage />
    </button>
    </template>

    <template v-if="shown('search')">
    <button :class="{ active: searchVisible }" :title="withKey($t('pdfReader.search'), K.search)" @click="emit('toggleSearch')">
      <IconSearch20Regular />
    </button>
    </template>
    <!-- Hand tool: the button shows the tool you switch TO (the cursor while the hand is on). -->
    <template v-if="shown('hand')">
    <button
      :aria-pressed="handTool"
      :title="handTool ? withKey($t('pdfReader.selectTool'), K.selectTool) : withKey($t('pdfReader.handTool'), K.handTool)"
      @click="emit('toggleHandTool')"
    >
      <IconPdfSelectTool v-if="handTool" />
      <IconPdfHandTool v-else />
    </button>
    </template>
    <template v-if="shown('edit')">
    <button :class="{ active: editVisible }" :aria-pressed="editVisible" :title="withKey($t('pdfReader.toggleEdit'), K.edit)" @click="emit('toggleEdit')">
      <IconEdit20Filled v-if="editVisible" />
      <IconEdit20Regular v-else />
    </button>
    </template>
    <template v-if="shown('ocr')">
    <button :class="{ active: ocrActive }" :aria-pressed="ocrActive" :title="withKey($t('pdfReader.ocr'), K.ocr)" @click="emit('toggleOcr')">
      <IconScanText20Regular />
    </button>
    </template>

    <!-- zoom -->
    <template v-if="shown('zoom')">
    <button
      :disabled="scale <= MIN_SCALE"
      :title="withKey($t('pdfReader.zoomOut', { zoom: Math.round(scale * 100) }), K.zoomOut)"
      @pointerdown="zoomRepeat.start(() => emit('zoomOut'))"
      @pointerup="zoomRepeat.stop"
      @pointerleave="zoomRepeat.stop"
      @pointercancel="zoomRepeat.stop"
    >
      <IconZoomOut20Regular />
    </button>
    <span ref="zoomBoxEl" class="page-box">
      <input
        v-model="zoomDraft"
        class="page-input"
        inputmode="decimal"
        :aria-label="$t('pdfReader.zoomLevel')"
        @keydown.enter="commitZoom"
        @keydown.esc.stop="discardZoom"
        @blur="commitZoom"
      />
    </span>
    <button
      :disabled="scale >= MAX_SCALE"
      :title="withKey($t('pdfReader.zoomIn', { zoom: Math.round(scale * 100) }), K.zoomIn)"
      @pointerdown="zoomRepeat.start(() => emit('zoomIn'))"
      @pointerup="zoomRepeat.stop"
      @pointerleave="zoomRepeat.stop"
      @pointercancel="zoomRepeat.stop"
    >
      <IconZoomIn20Regular />
    </button>
    </template>
    <template v-if="shown('preset')">
    <PdfIconDropdown :menu-title="withKey($t('pdfReader.menuFit'), K.fitCycle)" :options="presetOptions" :value="preset" @select="emit('setPreset', $event)" />
    </template>

    <!-- layout -->
    <template v-if="shown('scroll')">
    <PdfIconDropdown :menu-title="withKey($t('pdfReader.menuScroll'), K.scrollCycle)" :options="scrollOptions" :value="scrollMode" @select="emit('setScrollMode', $event)" />
    </template>
    <template v-if="shown('spread')">
    <PdfIconDropdown :menu-title="withKey($t('pdfReader.menuSpread'), K.spreadCycle)" :options="spreadOptions" :value="spreadMode" @select="emit('setSpreadMode', $event)" />
    </template>
    <template v-if="shown('rotate')">
    <button :title="withKey($t('pdfReader.rotateLeft'), K.rotateLeft)" @click="emit('rotate', -90)">
      <IconPdfRotateCounterclockwise />
    </button>
    <button :title="withKey($t('pdfReader.rotateRight'), K.rotateRight)" @click="emit('rotate', 90)">
      <IconPdfRotateClockwise />
    </button>
    </template>

    <template v-if="shown('presentation')">
    <button :title="withKey($t('pdfReader.presentation'), K.presentation)" @click="emit('presentation')">
      <IconPdfPresentation />
    </button>
    </template>
    <template v-if="shown('organize')">
    <button :aria-pressed="!!organizerOpen" :title="withKey($t('pdfOrganizer.toggle'), K.organizer)" @click="emit('toggleOrganizer')">
      <IconGrid20Regular />
    </button>
    </template>
    <template v-if="shown('save')">
    <button
      class="save-btn"
      :disabled="!canSave"
      :title="withKey($t('pdfReader.saveDoc.save'), K.save) + (dirty ? ` - ${$t('pdfReader.saveDoc.unsaved')}` : '')"
      @click="emit('save')"
    >
      <IconSave20Regular />
      <span v-if="dirty" class="dirty-dot" aria-hidden="true" />
    </button>
    </template>
    <template v-if="shown('saveas')">
    <button :title="withKey($t('pdfReader.saveDoc.saveAs'), K.saveAs)" @click="emit('saveAs')">
      <IconSaveEdit20Regular />
    </button>
    </template>
    <template v-if="shown('print')">
    <button :title="withKey($t('pdfReader.print'), K.print)" @click="emit('print')">
      <IconPrint20Regular />
    </button>
    </template>
    <template v-if="shown('properties')">
    <button :title="withKey($t('pdfReader.properties'), K.properties)" @click="emit('properties')">
      <IconPdfDocumentProperties />
    </button>
    </template>

    <!-- The book view's "more" button: one fixed place at the far end, shown once something has collapsed. -->
    <button
      ref="moreButton"
      class="overflow-btn"
      :disabled="!hasToolbarOverflow"
      :title="$t('pdfReader.more')"
      aria-haspopup="menu"
      @click="openMore"
    >
      <IconMoreHorizontal24Regular />
    </button>
    <ContextMenu ref="moreMenu" :items="moreItems" />
  </div>
</template>

<style scoped>
/* The shell, orientations, edge line and drag preview: assets/styles/readingToolbar.css. */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.page-box {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  flex-shrink: 0;
}
.page-input {
  width: 3.5em;
  height: 22px;
  text-align: center;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: transparent;
  color: inherit;
}
.page-input.labeled {
  width: 5em;
}
.overflow-btn {
  position: absolute;
  inset-inline-end: var(--toolbar-edge-inset);
  top: 50%;
  transform: translateY(-50%);
  background: var(--bg-toolbar);
  z-index: 1;
}
.overflow-btn:disabled {
  opacity: 0;
  visibility: hidden;
  cursor: default;
}
.toolbar-left .overflow-btn,
.toolbar-right .overflow-btn {
  top: auto;
  bottom: var(--toolbar-edge-inset);
  left: 50%;
  inset-inline-end: auto;
  transform: translateX(-50%);
}
.save-btn {
  position: relative;
}
/* Unsaved changes: a dot on the Save button. */
.dirty-dot {
  position: absolute;
  top: 5px;
  inset-inline-end: 5px;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent-color);
}
.page-count {
  opacity: 0.75;
  white-space: nowrap;
}
</style>
