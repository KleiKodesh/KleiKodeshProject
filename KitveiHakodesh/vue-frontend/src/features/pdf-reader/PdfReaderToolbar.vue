<script setup lang="ts">
import { useHostElementSize } from '@/composables/useHostResizeObserver'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
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
  IconComment20Regular,
  IconBookmarkAdd20Regular,
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
import { useSettingsStore } from '@/stores/settingsStore'
import IconPdfScrollPage from './icons/IconPdfScrollPage.vue'
import IconPdfScrollVertical from './icons/IconPdfScrollVertical.vue'
import IconPdfNavigation from './icons/IconPdfNavigation.vue'
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
import { choiceItems } from '@/composables/useToolbarMoreMenu'
import { TOOLBAR_BUTTON_SIZE } from '@/composables/useToolbarOverflow'
import { useGroupedToolbar, type GroupedToolbarItem } from '@/composables/useGroupedToolbar'
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
  /** Note mode is on (a click on a page places a note). */
  noteActive: boolean
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
  addNote: []
  toggleOcr: []
  toggleToc: [toggleButton: HTMLElement]
  presentation: []
  properties: []
  print: []
  addBookmark: []
  toggleOrganizer: []
  save: []
  saveAs: []
}>()

const hostWindow = useHostWindow()
const { t } = useI18n()
const zoomRepeat = usePressRepeat()

// The book view's toolbar shell and position (shared useToolbarDrag + app-wide toolbar position).
// The PDF viewer's own toolbar position (not the book view's): settingsStore.pdfToolbarPosition.
const { pdfToolbarPosition: toolbarPosition } = storeToRefs(useSettingsStore())
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
  'print', 'organize', 'presentation', 'rotate', 'spread', 'scroll', 'preset',
  'bookmark', 'note', 'edit', 'ocr', 'hand', 'zoom', 'first-last', 'search', 'save', 'toc', 'pages',
] as const
type OverflowKey = (typeof OVERFLOW_ORDER)[number]
/** The controls in the order the toolbar shows them, with their group, zone and width. */
const TOOLBAR_ITEMS: GroupedToolbarItem<OverflowKey>[] = [
  { key: 'toc', group: 'panels', zone: 'start' },
  { key: 'preset', group: 'layout', zone: 'start' },
  { key: 'scroll', group: 'layout', zone: 'start' },
  { key: 'spread', group: 'layout', zone: 'start' },
  { key: 'rotate', group: 'layout', zone: 'start', width: 2 },
  { key: 'presentation', group: 'document', zone: 'start' },
  { key: 'first-last', group: 'pages', zone: 'center', width: 2 },
  { key: 'pages', group: 'pages', zone: 'center', width: () => boxWidth('pages') },
  { key: 'search', group: 'panels', zone: 'center' },
  { key: 'hand', group: 'tools', zone: 'center' },
  { key: 'zoom', group: 'zoom', zone: 'center', width: () => boxWidth('zoom') },
  { key: 'bookmark', group: 'tools', zone: 'end' },
  { key: 'note', group: 'tools', zone: 'end' },
  { key: 'edit', group: 'tools', zone: 'end' },
  { key: 'ocr', group: 'tools', zone: 'end' },
  { key: 'organize', group: 'document', zone: 'end' },
  { key: 'save', group: 'document', zone: 'end' },
  { key: 'print', group: 'document', zone: 'end' },
]
// The page box ("1 / 364") and zoom box are measured, not guessed: a guess too wide collapses
// controls while there is still room. The last width is kept while a box sits in the menu.
const isVertical = computed(() => toolbarPosition.value === 'left' || toolbarPosition.value === 'right')
const pageBoxEl = ref<HTMLElement | null>(null)
const pageInputEl = ref<HTMLInputElement | null>(null)
function focusPageInput(e: MouseEvent) {
  if (e.target === pageInputEl.value) return
  e.preventDefault()
  pageInputEl.value?.focus()
  pageInputEl.value?.select()
}
const zoomBoxEl = ref<HTMLElement | null>(null)
// Measured along the toolbar's own axis: the width on a top/bottom toolbar, the height on a side one
// (there the boxes stack their parts and are about one button tall, not 80px wide).
const pageBoxSize = useHostElementSize(pageBoxEl, undefined, { box: 'border-box' })
const zoomBoxSize = useHostElementSize(zoomBoxEl, undefined, { box: 'border-box' })
const pageBoxW = computed(() => (isVertical.value ? pageBoxSize.height.value : pageBoxSize.width.value))
const zoomBoxW = computed(() => (isVertical.value ? zoomBoxSize.height.value : zoomBoxSize.width.value))
let lastPageBoxW = 78
let lastZoomBoxW = 46
let lastAxisVertical = false
/** The page / zoom box's room in button-widths: two buttons plus the measured box. */
function boxWidth(key: 'pages' | 'zoom'): number {
  // A switch of axis drops the other axis's remembered sizes.
  if (lastAxisVertical !== isVertical.value) {
    lastAxisVertical = isVertical.value
    lastPageBoxW = isVertical.value ? 40 : 78
    lastZoomBoxW = isVertical.value ? 24 : 46
  }
  if (key === 'zoom') {
    if (zoomBoxW.value > 0) lastZoomBoxW = zoomBoxW.value
    return 2 + lastZoomBoxW / TOOLBAR_BUTTON_SIZE
  }
  if (pageBoxW.value > 0) lastPageBoxW = pageBoxW.value
  return 2 + lastPageBoxW / TOOLBAR_BUTTON_SIZE
}
const { hasToolbarOverflow, overflowedInToolbarOrder, overflowGroupStarts, toolbarButtonVisible: shown } = useGroupedToolbar(
  toolbarEl,
  TOOLBAR_ITEMS,
  OVERFLOW_ORDER,
  // Save only while there is something to save: no room is kept for a button not drawn.
  (k) => k !== 'save' || !!props.canSave,
  isVertical,
)
// "Navigation: table of contents, bookmarks, comments, thumbnails, search results" - what the side panel holds.
const sidePanelTitle = computed(() =>
  `${t('pdfReader.sidePanel')}: ${['outline', 'bookmarks', 'comments', 'thumbnails', 'search'].map((m) => t(`pdfReader.panel.${m}`)).join(', ')}`)
const moreButton = ref<HTMLElement | null>(null)
const moreMenu = ref<InstanceType<typeof ContextMenu> | null>(null)
function openMore() {
  const r = moreButton.value?.getBoundingClientRect()
  if (r) void moreMenu.value?.showAtPosition(r.left, r.bottom)
}
function radio<V>(options: IconDropdownOption<V>[], value: V | null, pick: (v: V) => void): ContextMenuLeafItem[] {
  return choiceItems(options, value, pick, () => moreMenu.value?.hide())
}
/** One face per collapsed control, in collapse order; each runs the same emit as its toolbar button. */
const moreItems = computed<ContextMenuItem[]>(() => {
  const items: ContextMenuItem[] = []
  // in the toolbar's own order (the collapse order only decides WHAT goes)
  for (const key of overflowedInToolbarOrder.value) {
    // a separator between the toolbar's groups (document, layout, tools, zoom, pages, panels)
    if (items.length && overflowGroupStarts.value.has(key)) items.push({ type: 'separator' })
    if (key === 'save') {
      if (props.canSave) items.push({ icon: IconSave20Regular, label: t('pdfReader.saveDoc.save'), shortcut: K.save, action: () => emit('save') })
    } else if (key === 'print') items.push({ icon: IconPrint20Regular, label: t('pdfReader.print'), shortcut: K.print, action: () => emit('print') })
    else if (key === 'bookmark') items.push({ icon: IconBookmarkAdd20Regular, label: t('pdfReader.panel.addBookmarkShort'), title: t('pdfReader.panel.addBookmark'), shortcut: K.addBookmark, action: () => emit('addBookmark') })
    else if (key === 'organize') items.push({ type: 'checkbox', label: withKey(t('pdfOrganizer.toggle'), K.organizer), icon: IconGrid20Regular, checked: !!props.organizerOpen, onChange: () => { emit('toggleOrganizer'); moreMenu.value?.hide() } })
    else if (key === 'presentation') items.push({ icon: IconPdfPresentation, label: t('pdfReader.presentation'), shortcut: K.presentation, action: () => emit('presentation') })
    else if (key === 'rotate') {
      items.push({ icon: IconPdfRotateCounterclockwise, label: t('pdfReader.rotateLeft'), shortcut: K.rotateLeft, action: () => emit('rotate', -90) })
      items.push({ icon: IconPdfRotateClockwise, label: t('pdfReader.rotateRight'), shortcut: K.rotateRight, action: () => emit('rotate', 90) })
    } else if (key === 'spread') items.push({ type: 'submenu', icon: spreadOptions.value.find((o) => o.value === props.spreadMode)?.icon, label: withKey(t('pdfReader.menuSpread'), K.spreadCycle), items: radio(spreadOptions.value, props.spreadMode, (v) => emit('setSpreadMode', v)) })
    else if (key === 'scroll') items.push({ type: 'submenu', icon: scrollOptions.value.find((o) => o.value === props.scrollMode)?.icon, label: withKey(t('pdfReader.menuScroll'), K.scrollCycle), items: radio(scrollOptions.value, props.scrollMode, (v) => emit('setScrollMode', v)) })
    else if (key === 'preset') items.push({ type: 'submenu', icon: (presetOptions.value.find((o) => o.value === props.preset) ?? presetOptions.value[0])?.icon, label: withKey(t('pdfReader.menuFit'), K.fitCycle), items: radio(presetOptions.value, props.preset, (v) => emit('setPreset', v)) })
    else if (key === 'ocr') items.push({ type: 'checkbox', label: withKey(t('pdfReader.ocr'), K.ocr), icon: IconScanText20Regular, checked: props.ocrActive, onChange: () => { emit('toggleOcr'); moreMenu.value?.hide() } })
    else if (key === 'note') items.push({ type: 'checkbox', label: withKey(t('pdfReader.notes.add'), K.editTool.note), title: t('pdfReader.notes.tool'), icon: IconComment20Regular, checked: props.noteActive, onChange: () => { emit('addNote'); moreMenu.value?.hide() } })
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
      if (props.hasOutline) items.push({ type: 'checkbox', label: withKey(t('pdfReader.sidePanel'), K.toc), title: sidePanelTitle.value, icon: IconPdfNavigation, checked: props.tocVisible, onChange: () => { if (moreButton.value) emit('toggleToc', moreButton.value); moreMenu.value?.hide() } })
    } else if (key === 'pages') {
      items.push({ icon: IconChevronRight20Regular, label: t('pdfReader.prevPage'), shortcut: K.prevPage, action: () => emit('goToPage', props.pageNumber - 1) })
      items.push({ icon: IconChevronLeft20Regular, label: t('pdfReader.nextPage'), shortcut: K.nextPage, action: () => emit('goToPage', props.pageNumber + 1) })
    }
  }
  // Always in "more" (rare file actions): save as and document properties.
  if (items.length) items.push({ type: 'separator' })
  items.push({ icon: IconSaveEdit20Regular, label: t('pdfReader.saveDoc.saveAs'), shortcut: K.saveAs, action: () => emit('saveAs') })
  items.push({ icon: IconPdfDocumentProperties, label: t('pdfReader.properties'), shortcut: K.properties, action: () => emit('properties') })
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
    class="pdf-reader-toolbar reading-toolbar has-zones"
    dir="rtl"
    :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging, 'has-overflow': hasToolbarOverflow }]"
    :title="isOverBackground ? $t('bookView.toolbar.dragHint') : undefined"
  >
    <Teleport :to="hostWindow.document.body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>


    <!-- Three zones (RTL): view tools at the start (right), navigation / zoom / search in the centre,
         editing and file tools at the end (left). -->
    <div class="tb-zone tb-zone-start" data-toolbar-background>
    <template v-if="shown('toc')">
    <button
      :class="{ active: tocVisible }"
      :disabled="!hasOutline"
      :title="withKey(sidePanelTitle, K.toc)"
      @click="emit('toggleToc', $event.currentTarget as HTMLElement)"
    >
      <!-- navigation in the document (outline, bookmarks, comments, thumbnails): filled while open -->
      <IconPdfNavigation :filled="tocVisible" />
    </button>
    </template>
    <template v-if="shown('preset')">
    <PdfIconDropdown :menu-title="withKey($t('pdfReader.menuFit'), K.fitCycle)" :options="presetOptions" :value="preset" @select="emit('setPreset', $event)" />
    </template>
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
    </div>
    <div class="tb-zone tb-zone-center" data-toolbar-background>
    <template v-if="shown('first-last')">
    <button :disabled="pageNumber <= 1" :title="withKey($t('pdfReader.firstPage'), K.firstPage)" @click="emit('jumpToPage', 1)">
      <IconPdfFirstPage />
    </button>
    </template>
    <template v-if="shown('pages')">
    <button :disabled="pageNumber <= 1" :title="withKey($t('pdfReader.prevPage'), K.prevPage)" @click="emit('goToPage', pageNumber - 1)">
      <IconChevronRight20Regular class="dir-icon" />
    </button>
    <!-- One box to the eye ("27 / 112"); only the page number is typed in. A click on the
         count puts the caret in the number. -->
    <span ref="pageBoxEl" class="page-box" @mousedown="focusPageInput">
      <input
        ref="pageInputEl"
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
    </div>
    <div class="tb-zone tb-zone-end" data-toolbar-background>
    <template v-if="shown('bookmark')">
    <button :title="withKey($t('pdfReader.panel.addBookmark'), K.addBookmark)" @click="emit('addBookmark')">
      <IconBookmarkAdd20Regular />
    </button>
    </template>
    <template v-if="shown('note')">
    <button :class="{ active: noteActive }" :aria-pressed="noteActive" :title="withKey($t('pdfReader.notes.tool'), K.editTool.note)" @click="emit('addNote')">
      <IconComment20Regular />
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
    <template v-if="shown('organize')">
    <button :aria-pressed="!!organizerOpen" :title="withKey($t('pdfOrganizer.toggle'), K.organizer)" @click="emit('toggleOrganizer')">
      <IconGrid20Regular />
    </button>
    </template>
    <template v-if="canSave && shown('save')">
    <button
      class="save-btn"
      :title="withKey($t('pdfReader.saveDoc.save'), K.save) + (dirty ? ` - ${$t('pdfReader.saveDoc.unsaved')}` : '')"
      @click="emit('save')"
    >
      <IconSave20Regular />
      <span v-if="dirty" class="dirty-dot" aria-hidden="true" />
    </button>
    </template>
    <template v-if="shown('print')">
    <button :title="withKey($t('pdfReader.print'), K.print)" @click="emit('print')">
      <IconPrint20Regular />
    </button>
    </template>
    </div>

    <!-- The book view's "more" button: one fixed place at the far end; always there (save as, properties live in it). -->
    <button
      ref="moreButton"
      class="overflow-btn"
      :class="{ 'has-collapsed': hasToolbarOverflow }"
      :title="$t('pdfReader.more')"
      aria-haspopup="menu"
      @click="openMore"
    >
      <IconMoreHorizontal24Regular />
    </button>
    <ContextMenu ref="moreMenu" :items="moreItems" drill-in />
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
  gap: 2px;
  font-size: 12px;
  flex-shrink: 0;
  /* the frame is the box's, not the input's: number and count read as one field */
  height: 22px;
  padding-inline: 2px 6px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: text;
}
.page-box:focus-within {
  border-color: var(--accent-color);
}
.page-input {
  width: 3.5em;
  height: 100%;
  padding: 0;
  text-align: center;
  border: none;
  outline: none;
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
/* As the book view's: the opaque fill above replaces main.css's button background wholesale, so
   the hover wash (and its fade) is restated here, composited onto the toolbar's surface. */
.overflow-btn {
  transition:
    background 120ms,
    color 120ms;
}
.overflow-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}
.overflow-btn:disabled {
  opacity: 0;
  visibility: hidden;
  cursor: default;
}
/* The book view's vertical anchor: bottom edge, centred across. No logical inset here - in RTL
   'inset-inline-end: auto' is 'left: auto' and would undo the centring (the button fell off the bar). */
.toolbar-left .overflow-btn,
.toolbar-right .overflow-btn {
  left: 50%;
  right: auto;
  top: auto;
  bottom: var(--toolbar-edge-inset);
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
/* Side toolbar: the page and zoom boxes stack (input over its count) inside the narrow column. */
.toolbar-left .page-box,
.toolbar-right .page-box {
  flex-direction: column;
  gap: 1px;
  width: var(--toolbar-button-size);
  height: auto;
  /* padding, not margin: the measured border box (widthOf) must include it */
  padding: 2px 0;
}
.toolbar-left .page-input,
.toolbar-right .page-input,
.toolbar-left .page-input.labeled,
.toolbar-right .page-input.labeled {
  width: 100%;
  padding: 0;
  font-size: 11px;
}
.toolbar-left .page-count,
.toolbar-right .page-count {
  font-size: 10px;
  white-space: normal;
  text-align: center;
  line-height: 1.1;
}
</style>
