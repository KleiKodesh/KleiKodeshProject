<script setup lang="ts">
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import {
  IconZoomIn20Regular,
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconZoomOut20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconSearch20Regular,
  IconDocumentPdf20Regular,
  IconTimeline20Filled,
  IconLayoutColumnTwoFocusRight20Filled,
  IconLayoutColumnTwoFocusLeft20Filled,
  IconTimeline20Regular,
  IconDocumentMargins20Regular,
  IconDocumentMargins20Filled,
  IconMoreHorizontal24Regular,
  IconInfo20Regular,
  IconDismiss20Regular,
  IconCheckmark20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconPdfFirstPage from '@/features/pdf-reader/icons/IconPdfFirstPage.vue'
import IconPdfLastPage from '@/features/pdf-reader/icons/IconPdfLastPage.vue'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import WordDocumentGlyph from '@/components/WordDocumentGlyph.vue'
import { usePressRepeat } from '@/composables/usePressRepeat'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import ConversionActionButton, { type ConversionActionState } from '@/components/ConversionActionButton.vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useToolbarDrag } from '@/composables/useToolbarDrag'
import BookViewCleanTextCombo from '@/features/book-view/BookViewCleanTextCombo.vue'
import ToolbarSplitDropdown from '@/components/ToolbarSplitDropdown.vue'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import { choiceItems, submenuRow, useToolbarMoreMenu } from '@/composables/useToolbarMoreMenu'
import { CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS } from '@/features/book-view/useBookViewToolbarOverflow'
import { useGroupedToolbar, type GroupedToolbarItem } from '@/composables/useGroupedToolbar'

const props = defineProps<{
  tocVisible: boolean
  notesVisible: boolean
  notesSync: boolean
  /** Where the notes pane sits; the side places only on a wide page (as the book view's side commentary). */
  notesPlace: 'bottom' | 'right' | 'left'
  canPlaceNotesBeside: boolean
  hasHeadings: boolean
  hasNotes: boolean
  zoom: number
  /** The notes pane's own zoom (the book view's commentary zoom). */
  notesZoom: number
  pdfState: ConversionActionState
  searchVisible: boolean
  canOpenAsPdf: boolean
  /** The text column ignores the document's page width / margins (global setting). */
  ignoreMargins: boolean
}>()

const emit = defineEmits<{
  toggleToc: [toggleButton: HTMLElement]
  toggleNotes: []
  setNotesPlace: [place: 'bottom' | 'right' | 'left']
  toggleIgnoreMargins: []
  toggleNotesSync: []
  zoomIn: []
  zoomOut: []
  openAsPdf: []
  cancelOpenAsPdf: []
  openConvertedPdf: []
  openInDefaultApp: []
  toggleSearch: []
  showDetails: []
  navigateToNextSection: []
  navigateToPreviousSection: []
  goToStart: []
  goToEnd: []
}>()

const zoomRepeat = usePressRepeat()

// Dragged by its background to any edge of the page, as the book view's toolbar (the shared
// useToolbarDrag and the same app-wide toolbar position setting).
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)
const toolbarEl = ref<HTMLElement | null>(null)
const { isDragging, dropPreviewStyle, isOverBackground } = useToolbarDrag(toolbarEl, toolbarPosition, '.fast-doc-view')

const notesPlaceMenu = ref<InstanceType<typeof ToolbarSplitDropdown> | null>(null)
const NOTES_PLACES = [
  { place: 'bottom', labelKey: 'fastDocView.notesPlaceBottom' },
  { place: 'right', labelKey: 'fastDocView.notesPlaceRight' },
  { place: 'left', labelKey: 'fastDocView.notesPlaceLeft' },
] as const
function pickNotesPlace(place: 'bottom' | 'right' | 'left') {
  emit('setNotesPlace', place)
  notesPlaceMenu.value?.close()
}
/** The place the pane shows in now (a side place falls back to the bottom on a narrow page). */
function shownNotesPlace() {
  return props.canPlaceNotesBeside ? props.notesPlace : 'bottom'
}

function onToggleToc(event: MouseEvent) {
  emit('toggleToc', event.currentTarget as HTMLElement)
}
// The window this is drawn in: its menus and dialogs open there (hostWindow.ts).
const hostWindow = useHostWindow()
const { t } = useI18n()

// -- Groups, zones and the "more" menu (shared useGroupedToolbar, as the book view and PDF) --
// RTL: navigation at the start (right); search, display and the notes pane in the centre;
// the file's other ways to open at the end (left).
type FastDocToolbarKey =
  | 'toc' | 'section-nav' | 'first-last' | 'search' | 'zoom' | 'clean-text' | 'ignore-margins'
  | 'notes' | 'notes-sync' | 'open-as-pdf' | 'open-in-default-app'
const TOOLBAR_ITEMS: readonly GroupedToolbarItem<FastDocToolbarKey>[] = [
  { key: 'toc', group: 'navigation', zone: 'start' },
  { key: 'section-nav', group: 'navigation', zone: 'start', width: 2 },
  { key: 'first-last', group: 'navigation', zone: 'start', width: 2 },
  { key: 'search', group: 'search', zone: 'center' },
  { key: 'zoom', group: 'display', zone: 'center', width: 2 },
  // The notes split button has the clean-text combo's shape (button + chevron).
  { key: 'notes', group: 'notes', zone: 'center', width: CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS },
  { key: 'notes-sync', group: 'notes', zone: 'center' },
  { key: 'clean-text', group: 'view', zone: 'end', width: CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS },
  { key: 'ignore-margins', group: 'view', zone: 'end' },
  { key: 'open-as-pdf', group: 'open', zone: 'end' },
  { key: 'open-in-default-app', group: 'open', zone: 'end' },
]
/** First key = first to collapse into "more": other ways to open first, reading navigation last. */
const TOOLBAR_OVERFLOW_ORDER: readonly FastDocToolbarKey[] = [
  'open-in-default-app', 'open-as-pdf', 'ignore-margins', 'notes-sync', 'clean-text',
  'zoom', 'notes', 'search', 'first-last', 'section-nav', 'toc',
]
/** Must match each control's v-if, or the fit counts room for a control that is not drawn. */
function isPresent(key: FastDocToolbarKey): boolean {
  return key === 'open-as-pdf' ? props.canOpenAsPdf : true
}
const isVerticalToolbar = computed(() => toolbarPosition.value === 'left' || toolbarPosition.value === 'right')
const { hasToolbarOverflow, overflowGroupStarts, overflowedInToolbarOrder, toolbarButtonVisible } = useGroupedToolbar(
  toolbarEl, TOOLBAR_ITEMS, TOOLBAR_OVERFLOW_ORDER, isPresent, isVerticalToolbar,
)
/** What a control on the toolbar wears so it can collapse into "more". */
function collapseAttrs(key: FastDocToolbarKey) {
  const visible = toolbarButtonVisible(key)
  return { class: { collapsible: true, collapsed: !visible }, inert: !visible }
}
const overflowButtonEl = ref<HTMLElement | null>(null)
const cleanTextRef = ref<InstanceType<typeof BookViewCleanTextCombo> | null>(null)
const more = useToolbarMoreMenu(toolbarPosition)
function openMore() {
  more.openFrom(overflowButtonEl.value)
}
const ZOOM_LABEL = computed(() => `${Math.round(props.zoom)}% | ${Math.round(props.notesZoom)}%`)
const zoomOutTitle = computed(() => t('fastDocView.zoomOut', { text: Math.round(props.zoom), notes: Math.round(props.notesZoom) }))
const zoomInTitle = computed(() => t('fastDocView.zoomIn', { text: Math.round(props.zoom), notes: Math.round(props.notesZoom) }))
const notesSyncTitle = computed(() => (props.notesSync ? t('fastDocView.syncNotesOnTitle') : t('fastDocView.syncNotesOffTitle')))

/** The notes control as a row of "more": its on/off switch, then where the pane sits (a side place needs a wide page). */
function notesMenuRow() {
  const placeChoices = NOTES_PLACES.map((o) => ({ value: o.place, label: t(o.labelKey), disabled: o.place !== 'bottom' && !props.canPlaceNotesBeside }))
  return submenuRow(
    t('fastDocView.notes'),
    !props.notesVisible ? IconLayoutRowTwo20Regular : shownNotesPlace() === 'right' ? IconLayoutColumnTwoFocusRight20Filled : shownNotesPlace() === 'left' ? IconLayoutColumnTwoFocusLeft20Filled : IconLayoutRowTwoFocusBottom20Filled,
    [
      { type: 'checkbox', label: t('fastDocView.notes'), checked: props.notesVisible, disabled: !props.hasNotes, onChange: () => { more.hide(); emit('toggleNotes') } },
      { type: 'heading', label: t('fastDocView.notesPlace') },
      ...choiceItems(placeChoices, props.notesPlace, (place) => emit('setNotesPlace', place), () => more.hide()),
    ],
    { disabled: !props.hasNotes },
  )
}

/**
 * The "more" menu, as the PDF toolbar's: one row per collapsed control, in toolbar order, a
 * separator between groups; each row runs the same emit its toolbar button does. A dropdown
 * control (clean text, notes place) is a row with a submenu arrow; picking an item applies it.
 */
const moreItems = computed<ContextMenuItem[]>(() => {
  const items: ContextMenuItem[] = []
  const hide = () => more.hide()
  const firstLine = (text: string) => text.split('\n')[0] ?? text
  for (const key of overflowedInToolbarOrder.value) {
    if (items.length && overflowGroupStarts.value.has(key)) items.push({ type: 'separator' })
    switch (key) {
      case 'toc':
        // The pane's toggle anchor is the "more" button (the TOC button is collapsed).
        items.push({ type: 'checkbox', icon: IconTreeRtl, label: t('fastDocView.toc'), checked: props.tocVisible, disabled: !props.hasHeadings, onChange: () => { hide(); if (overflowButtonEl.value) emit('toggleToc', overflowButtonEl.value) } })
        break
      case 'section-nav':
        items.push({ icon: IconChevronRight20Regular, label: t('bookView.toolbar.prevSection'), disabled: !props.hasHeadings, action: () => emit('navigateToPreviousSection'), keepOpen: true })
        items.push({ icon: IconChevronLeft20Regular, label: t('bookView.toolbar.nextSection'), disabled: !props.hasHeadings, action: () => emit('navigateToNextSection'), keepOpen: true })
        break
      case 'first-last':
        items.push({ icon: IconPdfFirstPage, label: t('bookView.toolbar.goToStart'), action: () => emit('goToStart') })
        items.push({ icon: IconPdfLastPage, label: t('bookView.toolbar.goToEnd'), action: () => emit('goToEnd') })
        break
      case 'search':
        items.push({ type: 'checkbox', icon: IconSearch20Regular, label: t('fastDocView.search'), checked: props.searchVisible, onChange: () => { hide(); emit('toggleSearch') } })
        break
      case 'zoom':
        items.push({ type: 'heading', label: ZOOM_LABEL.value })
        items.push({ icon: IconZoomOut20Regular, label: firstLine(zoomOutTitle.value), title: zoomOutTitle.value, disabled: props.zoom <= ZOOM_CONFIG.MIN && props.notesZoom <= ZOOM_CONFIG.MIN, action: () => emit('zoomOut'), keepOpen: true })
        items.push({ icon: IconZoomIn20Regular, label: firstLine(zoomInTitle.value), title: zoomInTitle.value, disabled: props.zoom >= ZOOM_CONFIG.MAX && props.notesZoom >= ZOOM_CONFIG.MAX, action: () => emit('zoomIn'), keepOpen: true })
        break
      case 'notes':
        items.push(notesMenuRow())
        break
      case 'notes-sync':
        items.push({ type: 'checkbox', icon: props.notesSync ? IconTimeline20Filled : IconTimeline20Regular, label: firstLine(notesSyncTitle.value), title: notesSyncTitle.value, checked: props.notesSync, disabled: !props.hasNotes, onChange: () => { emit('toggleNotesSync'); hide() } })
        break
      case 'clean-text':
        if (cleanTextRef.value) items.push(cleanTextRef.value.moreMenuRow())
        break
      case 'ignore-margins':
        items.push({ type: 'checkbox', icon: props.ignoreMargins ? IconDocumentMargins20Filled : IconDocumentMargins20Regular, label: t('fastDocView.ignoreMargins'), checked: props.ignoreMargins, onChange: () => { emit('toggleIgnoreMargins'); hide() } })
        break
      case 'open-as-pdf':
        if (props.canOpenAsPdf) {
          if (props.pdfState === 'busy') items.push({ icon: IconDismiss20Regular, label: t('fastDocView.cancelOpenAsPdf'), action: () => emit('cancelOpenAsPdf') })
          else if (props.pdfState === 'done') items.push({ icon: IconCheckmark20Regular, label: t('fastDocView.openConvertedPdf'), action: () => emit('openConvertedPdf') })
          else items.push({ icon: IconDocumentPdf20Regular, label: t('fastDocView.openAsPdf'), action: () => emit('openAsPdf') })
        }
        break
      case 'open-in-default-app':
        items.push({ icon: WordDocumentGlyph, label: t('fastDocView.openInDefaultApp'), action: () => emit('openInDefaultApp') })
        break
    }
  }
  // Always here, never on the bar: what is known about the open document.
  if (items.length) items.push({ type: 'separator' })
  items.push({ icon: IconInfo20Regular, label: t('fastDocView.details'), action: () => emit('showDetails') })
  return items
})
</script>

<template>
  <!-- The book view toolbar's shell (.reading-toolbar, assets/styles/readingToolbar.css): any edge, dragged by its background. -->
  <div
    ref="toolbarEl"
    class="fast-doc-view-toolbar reading-toolbar has-zones"
    dir="rtl"
    :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging, 'has-overflow': hasToolbarOverflow }]"
    :title="isOverBackground ? $t('bookView.toolbar.dragHint') : undefined"
  >
    <Teleport :to="hostWindow.document.body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>
    <!-- Three zones (RTL), as the book view: navigation at the start (right); search, display and
         the notes pane in the centre; the file's other ways to open at the end (left). -->
    <div class="tb-zone tb-zone-start" data-toolbar-background>
      <button
        v-bind="collapseAttrs('toc')"
        :class="{ active: tocVisible }"
        :disabled="!hasHeadings"
        :title="$t('fastDocView.toc')"
        @click="onToggleToc"
      >
        <IconTreeRtl />
      </button>
      <!-- The book view's previous / next section buttons: here the previous / next heading. -->
      <button
        v-bind="collapseAttrs('section-nav')"
        :disabled="!hasHeadings"
        :title="$t('bookView.toolbar.prevSection')"
        @click="emit('navigateToPreviousSection')"
      >
        <IconChevronRight20Regular class="dir-icon" />
      </button>
      <button
        v-bind="collapseAttrs('section-nav')"
        :disabled="!hasHeadings"
        :title="$t('bookView.toolbar.nextSection')"
        @click="emit('navigateToNextSection')"
      >
        <IconChevronLeft20Regular class="dir-icon" />
      </button>
      <button v-bind="collapseAttrs('first-last')" :title="$t('bookView.toolbar.goToStart')" @click="emit('goToStart')">
        <IconPdfFirstPage />
      </button>
      <button v-bind="collapseAttrs('first-last')" :title="$t('bookView.toolbar.goToEnd')" @click="emit('goToEnd')">
        <IconPdfLastPage />
      </button>
    </div>
    <div class="tb-zone tb-zone-center" data-toolbar-background>
      <button
        v-bind="collapseAttrs('search')"
        :class="{ active: searchVisible }"
        :title="$t('fastDocView.search')"
        @click="emit('toggleSearch')"
      >
        <IconSearch20Regular />
      </button>
      <!-- hold to repeat, as in the book view (shared usePressRepeat) -->
      <button
        v-bind="collapseAttrs('zoom')"
        :title="zoomOutTitle"
        :disabled="zoom <= ZOOM_CONFIG.MIN && notesZoom <= ZOOM_CONFIG.MIN"
        @pointerdown="zoomRepeat.start(() => emit('zoomOut'))"
        @pointerup="zoomRepeat.stop"
        @pointerleave="zoomRepeat.stop"
        @pointercancel="zoomRepeat.stop"
      >
        <IconZoomOut20Regular />
      </button>
      <button
        v-bind="collapseAttrs('zoom')"
        :title="zoomInTitle"
        :disabled="zoom >= ZOOM_CONFIG.MAX && notesZoom >= ZOOM_CONFIG.MAX"
        @pointerdown="zoomRepeat.start(() => emit('zoomIn'))"
        @pointerup="zoomRepeat.stop"
        @pointerleave="zoomRepeat.stop"
        @pointercancel="zoomRepeat.stop"
      >
        <IconZoomIn20Regular />
      </button>
      <!-- The notes split button (the clean-text combo's shared ToolbarSplitDropdown): the main half opens /
           closes the pane in the chosen place, the dropdown picks bottom / right / left (sides on a wide page only). -->
      <ToolbarSplitDropdown
        ref="notesPlaceMenu"
        v-bind="collapseAttrs('notes')"
        class="dropdown-host fast-doc-view-notes-place"
        :active="notesVisible"
        :disabled="!hasNotes"
        :main-title="$t('fastDocView.notes')"
        :options-title="$t('fastDocView.notesPlace')"
        @toggle="emit('toggleNotes')"
      >
        <template #icon>
          <IconLayoutRowTwo20Regular v-if="!notesVisible" />
          <IconLayoutColumnTwoFocusRight20Filled v-else-if="shownNotesPlace() === 'right'" />
          <IconLayoutColumnTwoFocusLeft20Filled v-else-if="shownNotesPlace() === 'left'" />
          <IconLayoutRowTwoFocusBottom20Filled v-else />
        </template>
        <template #label>{{ $t('fastDocView.notes') }}</template>
        <template v-for="(option, i) in NOTES_PLACES" :key="option.place">
          <div v-if="i > 0" class="notes-place-separator" role="separator" />
          <button
            class="option-row"
            role="menuitemradio"
            :aria-checked="notesPlace === option.place"
            :disabled="option.place !== 'bottom' && !canPlaceNotesBeside"
            @click="pickNotesPlace(option.place)"
          >
            <input type="radio" tabindex="-1" :checked="notesPlace === option.place" :disabled="option.place !== 'bottom' && !canPlaceNotesBeside" />
            <span>{{ $t(option.labelKey) }}</span>
          </button>
        </template>
      </ToolbarSplitDropdown>
      <!-- the book view's sync-commentaries toggle (same icon and tooltips), for the notes pane -->
      <button
        v-bind="collapseAttrs('notes-sync')"
        :disabled="!hasNotes"
        :class="{ active: notesSync }"
        :title="notesSyncTitle"
        @click="emit('toggleNotesSync')"
      >
        <IconTimeline20Filled v-if="notesSync" />
        <IconTimeline20Regular v-else />
      </button>
    </div>
    <div class="tb-zone tb-zone-end" data-toolbar-background>
      <!-- the book view's clean-text combo (Ctrl+D): the same global setting and options dropdown -->
      <BookViewCleanTextCombo ref="cleanTextRef" v-bind="collapseAttrs('clean-text')" class="dropdown-host" />
      <!-- Ignore the document's page width / margins: the book view's reading-column width (global setting). -->
      <button
        v-bind="collapseAttrs('ignore-margins')"
        class="fast-doc-view-ignore-margins"
        :class="{ active: ignoreMargins }"
        :aria-pressed="ignoreMargins"
        :title="$t('fastDocView.ignoreMargins')"
        @click="emit('toggleIgnoreMargins')"
      >
        <IconDocumentMargins20Filled v-if="ignoreMargins" />
        <IconDocumentMargins20Regular v-else />
      </button>
      <!-- The same file through the Word -> PDF conversion, on request only: while it runs the
           button is an animated X that cancels it; when done a check that opens the PDF. -->
      <ConversionActionButton
        v-if="canOpenAsPdf"
        v-bind="collapseAttrs('open-as-pdf')"
        class="fast-doc-view-open-as-pdf"
        :state="pdfState"
        :idle-title="$t('fastDocView.openAsPdf')"
        :busy-title="$t('fastDocView.cancelOpenAsPdf')"
        :done-title="$t('fastDocView.openConvertedPdf')"
        @start="emit('openAsPdf')"
        @cancel="emit('cancelOpenAsPdf')"
        @open="emit('openConvertedPdf')"
      >
        <IconDocumentPdf20Regular />
      </ConversionActionButton>
      <!-- The file in whatever program the system opens it with (shell open, in the user's own process). -->
      <button
        v-bind="collapseAttrs('open-in-default-app')"
        class="fast-doc-view-open-in-default-app"
        :title="$t('fastDocView.openInDefaultApp')"
        @click="emit('openInDefaultApp')"
      >
        <WordDocumentGlyph />
      </button>
    </div>
    <!-- "more": out of the flex flow, pinned to the toolbar's far end; shown once something collapsed
         (this toolbar has no row that lives only in "more"). -->
    <button
      ref="overflowButtonEl"
      class="overflow-btn"
      :title="$t('bookView.toolbar.more')"
      aria-haspopup="menu"
      @click="openMore"
    >
      <IconMoreHorizontal24Regular />
    </button>
    <!-- The toolbar's "more" menu: the PDF toolbar's ContextMenu, items built from data (moreItems). -->
    <ContextMenu :ref="more.menuRef" :items="moreItems" drill-in />
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
/* Collapsing into "more": the control stays in the flow at zero size (no jolt), as the book view's. */
.collapsible {
  overflow: hidden;
}
.toolbar-top .collapsible.collapsed,
.toolbar-bottom .collapsible.collapsed {
  width: 0;
  margin-inline: 0;
  padding-inline: 0;
  opacity: 0;
}
.toolbar-left .collapsible.collapsed,
.toolbar-right .collapsible.collapsed {
  height: 0;
  margin-block: 0;
  padding-block: 0;
  opacity: 0;
}
.collapsible.collapsed {
  pointer-events: none;
}
.collapsible.dropdown-host:not(.collapsed) {
  overflow: visible;
}
/* "more": out of the flow, pinned to the toolbar's far end (the book view's .overflow-btn). */
.overflow-btn {
  position: absolute;
  inset-inline-end: var(--toolbar-edge-inset);
  top: 50%;
  transform: translateY(-50%);
  background: var(--bg-toolbar);
  z-index: 1;
  transition:
    background 120ms,
    color 120ms;
}
.overflow-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}
@media (prefers-reduced-motion: reduce) {
  .overflow-btn {
    transition: none;
  }
}
.toolbar-left .overflow-btn,
.toolbar-right .overflow-btn {
  left: 50%;
  top: auto;
  bottom: var(--toolbar-edge-inset);
  transform: translateX(-50%);
}
/* the notes-place dropdown rows are buttons: undo the toolbar button sizing above */
.fast-doc-view-notes-place :deep(.option-row) {
  width: 100%;
  height: 28px;
  justify-content: flex-start;
  border-radius: 0;
}
.fast-doc-view-notes-place :deep(.option-row input) {
  margin: 0;
  pointer-events: none;
}
.fast-doc-view-notes-place :deep(.notes-place-separator) {
  height: 1px;
  margin: 2px 0;
  background: var(--border-color);
}
</style>
