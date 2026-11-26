<script setup lang="ts">
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { computed, ref, nextTick } from 'vue'
import { useDebounceFn, useEventListener } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import {
  IconSearch20Regular,
  IconInfo20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutColumnTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconLayoutColumnTwoFocusRight20Filled,
  IconLayoutColumnTwoFocusLeft20Filled,
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconTimeline20Regular,
  IconTimeline20Filled,
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconMoreHorizontal24Regular,
  IconTextAlignRight20Regular,
  IconTextAlignJustify20Regular,
  IconReadAloud20Regular,
  IconReadAloud20Filled,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import BookViewRelatedBooksDropdown from './BookViewRelatedBooksDropdown.vue'
import BookViewBookmarksDropdown from './BookViewBookmarksDropdown.vue'
import type { BookBookmark } from '@/stores/bookBookmarks'
import type { BookViewLinesBookmarkPoint } from './lines/useBookViewLinesBookmarkPoint'
import BookViewVersionsDropdown from './BookViewVersionsDropdown.vue'
import BookViewHebrewBooksEditionsDropdown from './BookViewHebrewBooksEditionsDropdown.vue'
import BookViewBookDetailsDialog from './BookViewBookDetailsDialog.vue'
import BookViewWordLinkMarkersDropdown from './BookViewWordLinkMarkersDropdown.vue'
import BookViewCleanTextCombo from './BookViewCleanTextCombo.vue'
import BookViewExportToWordGlyph from '@/components/WordDocumentGlyph.vue'
import { usePressRepeat } from '@/composables/usePressRepeat'
import ConversionActionButton from '@/components/ConversionActionButton.vue'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import { useToolbarMoreMenu } from '@/composables/useToolbarMoreMenu'
import { useBookViewToolbarOverflow, type ToolbarOverflowKey } from './useBookViewToolbarOverflow'
import { useToolbarDrag } from '@/composables/useToolbarDrag'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { LinesTable } from './lines/linesTable'
import type { BookVersionRow, HebrewBooksEdition } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'
import IconPdfFirstPage from '@/features/pdf-reader/icons/IconPdfFirstPage.vue'
import IconPdfLastPage from '@/features/pdf-reader/icons/IconPdfLastPage.vue'
// The window this is drawn in (an extra app window draws its own, hostWindow.ts).
const hostWindow = useHostWindow()

const { t } = useI18n()

const props = defineProps<{
  searchVisible: boolean
  tocVisible: boolean
  hasToc: boolean
  hasCommentaries: boolean
  hasRelatedBooks: boolean
  // The active tab — zoom actions and the displayed percentages are
  // scoped to it, so a toolbar never reads or writes another tab's zoom.
  tabId?: string
  bookId: number | undefined
  /** The publisher forbids editable export of this book (Core's book protection). */
  exportRestricted?: boolean
  // True while an export to Word runs; the button shows a spinner.
  exportingToWord?: boolean
  // The book's alternate versions. Empty for most books, which hides the control.
  versions: BookVersionRow[]
  activeVersionId: number | null
  // The book's scanned editions on HebrewBooks (catalog suggestions; often empty). The control
  // shows in every book regardless: it also lists the reader's own items and the add options.
  hebrewBooksEditions: HebrewBooksEdition[]
  /** Otzaria's scanned tractate PDF on this computer (Talmud Bavli), or null. */
  hebrewBooksTalmudPdfPath: string | null
  currentScrollLineIndex: number
  lines: LinesTable
  /** The default name for a bookmark on a line - its TOC path. */
  bookmarkLabelForLine?: (lineIndex: number) => string
  /** Where a bookmark made now points - the lines pane reads the selection or top of view. */
  readBookmarkPoint?: () => BookViewLinesBookmarkPoint | null
  bottomCommentaryVisible?: boolean
  sideCommentaryVisible?: boolean
  sideLeftCommentaryVisible?: boolean
  // The side panels need a pane wide enough to hold them; on a narrow pane their
  // buttons are hidden entirely rather than offering a mode that cannot apply.
  canUseSidePanel?: boolean
  /** Whether the read-aloud panel is open. */
  readAloudOpen?: boolean
}>()
const emit = defineEmits<{ toggleReadAloud: []; toggleBottomCommentary: []; toggleSideCommentary: []; toggleSideLeftCommentary: []; toggleSearch: []; toggleToc: []; exportToWord: []; cancelExportToWord: []; navigateToNextSection: []; navigateToPreviousSection: []; goToStart: []; goToEnd: []; selectVersion: [versionId: number | null]; goToBookmark: [bookmark: BookBookmark] }>()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom, commentaryZoom, toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

/** The export control's name - its tooltip on the toolbar, its text in the flyout. */
const EXPORT_TO_WORD_LABEL = t('bookView.toolbar.exportToWord')
const EXPORT_RESTRICTED_LABEL = t('bookView.exportRestricted')

/** The overflow button's name - the same word the nav rail's overflow button wears. */
const MORE_LABEL = t('bookView.toolbar.more')

/**
 * The name the sync-commentaries row shows. It is the first line of that control's own
 * tooltip - the tooltip carries the name followed by an explanation, and a row wants just
 * the name.
 */
const SYNC_COMMENTARIES_LABEL = t('bookView.toolbar.syncCommentaries')

/** Paragraph view: the lines between headings run on as one paragraph. One app-wide setting. */
const flowText = computed(() => settingsStore.linesLayout === 'flow')
/** Says what a click does, so it names the other state. */
const flowTextLabel = computed(() => t(flowText.value ? 'bookView.toolbar.flowTextOff' : 'bookView.toolbar.flowTextOn'))
function toggleFlowText() {
  settingsStore.linesLayout = flowText.value ? 'rows' : 'flow'
}

/** Read aloud lives in its own panel (BookViewReadAloudPanel); this button opens and closes it. */
const readAloudSupported = typeof window !== 'undefined' && 'speechSynthesis' in window
const readAloudLabel = computed(() => t('bookView.toolbar.readAloud'))
/** The toolbar button's tooltip: the name, then what it reads, on new lines. */
const readAloudTitle = computed(() => `${t('bookView.toolbar.readAloud')}
${t('bookView.toolbar.readAloudHint')}`)

const tocBtnRef = ref<HTMLElement | null>(null)
const bookmarksDropdownRef = ref<InstanceType<typeof BookViewBookmarksDropdown> | null>(null)
const relatedBooksRef = ref<InstanceType<typeof BookViewRelatedBooksDropdown> | null>(null)
const versionsRef = ref<InstanceType<typeof BookViewVersionsDropdown> | null>(null)
const hebrewBooksEditionsRef = ref<InstanceType<typeof BookViewHebrewBooksEditionsDropdown> | null>(null)
const wordLinkMarkersRef = ref<InstanceType<typeof BookViewWordLinkMarkersDropdown> | null>(null)
const cleanTextRef = ref<InstanceType<typeof BookViewCleanTextCombo> | null>(null)

/**
 * Ctrl+Shift+D and the lines' context menu: opens the bookmarks dropdown on a new bookmark,
 * its name field focused. With bookmarks collapsed into "more" there is no name field: the
 * bookmark is saved with its suggested name (the TOC path) and shows in the menu's list.
 *
 * The point is read first, before anything moves focus off the selection. Then the control is
 * counted as present BEFORE the toolbar decides where it goes: on a book's first bookmark it
 * is not on the toolbar yet, and opening it there would add it to the count only after the
 * choice was made - on a narrow pane it then collapses in the same tick, taking the panel and
 * its name field with it, invisible and inert.
 */
async function createBookmark() {
  const point = props.readBookmarkPoint?.() ?? null
  bookmarksPresent.value = true
  await nextTick()
  if (toolbarButtonVisible('bookmarks')) {
    void bookmarksDropdownRef.value?.createBookmark(point)
    return
  }
  void bookmarksDropdownRef.value?.addBookmark(point)
}

// Continuous zoom: the shared hold-to-repeat (usePressRepeat) - a step on pointerdown, then
// every 80 ms after 400 ms held; stopped on release and on unmount.
function zoomStep(direction: 'in' | 'out') {
  if (direction === 'in') bookViewStore.zoomIn(props.tabId, props.bookId)
  else bookViewStore.zoomOut(props.tabId, props.bookId)
}
const zoomRepeat = usePressRepeat()
function startContinuousZoom(direction: 'in' | 'out') {
  zoomRepeat.start(() => zoomStep(direction))
}
const stopContinuousZoom = zoomRepeat.stop

const autoSelectTopLineTitle = computed(() =>
  autoSelectTopLine.value
    ? t('bookView.toolbar.syncOnTitle')
    : t('bookView.toolbar.syncOffTitle'),
)

// This tab's zoom percentages, read directly by tab and book id.
const linesZoomPct = computed(() =>
  props.tabId != null && props.bookId != null
    ? bookViewStore.getLinesZoom(props.tabId, props.bookId)
    : zoom.value,
)
// One label for every panel's zoom: a single number while they all agree, one
// number per panel once any panel has been zoomed apart from the rest.
const commentaryZoomLabel = computed(() => {
  if (props.tabId == null || props.bookId == null) return `${Math.round(commentaryZoom.value)}%`
  const zooms = COMMENTARY_SLOTS.map((slot) =>
    Math.round(bookViewStore.getCommentaryZoom(props.tabId!, props.bookId!, slot)),
  )
  return zooms.every((z) => z === zooms[0])
    ? `${zooms[0]}%`
    : zooms.map((z) => `${z}%`).join(' / ')
})

/**
 * The zoom row's heading. The pair has no single name - its two buttons are opposite
 * directions of one action - so the row is headed by what the zoom is currently SET to,
 * which is the one thing true of both buttons and is what the tooltips already report.
 */
const ZOOM_LABEL = computed(
  () => `${Math.round(linesZoomPct.value)}% | ${commentaryZoomLabel.value}`,
)

const zoomOutTitle = computed(
  () => t('bookView.toolbar.zoomOutTitle', { text: Math.round(linesZoomPct.value), commentary: commentaryZoomLabel.value }),
)

const zoomInTitle = computed(
  () => t('bookView.toolbar.zoomInTitle', { text: Math.round(linesZoomPct.value), commentary: commentaryZoomLabel.value }),
)

// The panels are independent, so each title names its own panel in both states —
// a shared "close the commentary panel" phrase left them indistinguishable. The
// two side panels are named by their edge, not just "בצד".
const bottomCommentaryTitle = computed(() =>
  props.bottomCommentaryVisible
    ? t('bookView.toolbar.close_bottom')
    : t('bookView.toolbar.open_bottom'),
)

// While a side column shows a scanned edition instead of commentary, its button says so.
const sideEditionStore = useHebrewBooksSideEditionStore()
const { slot: editionSlot } = useHebrewBooksSideEditionMode()
function showsEdition(slot: 'side' | 'side-left'): boolean {
  return sideEditionStore.isEditionShown() && editionSlot.value === slot
}

const sideCommentaryTitle = computed(() =>
  props.sideCommentaryVisible
    ? t(showsEdition('side') ? 'bookView.toolbar.close_right_edition' : 'bookView.toolbar.close_right')
    : t('bookView.toolbar.open_right'),
)

const sideLeftCommentaryTitle = computed(() =>
  props.sideLeftCommentaryVisible
    ? t(showsEdition('side-left') ? 'bookView.toolbar.close_left_edition' : 'bookView.toolbar.close_left')
    : t('bookView.toolbar.open_left'),
)

// ── Overflow ───────────────────────────────────────────────────────────────────
//
// A pane too narrow (or, for a side toolbar, too short) for every button collapses its
// controls into a "more" flyout, worst-first in the order TOOLBAR_OVERFLOW_ORDER fixes. The
// composable owns the fit arithmetic; what stays here is which of those controls this book
// actually has, and the flyout's state.
const toolbarEl = ref<HTMLElement | null>(null)

// While the window is being resized, collapses happen at once: a 260ms slide under a
// moving window edge reads as the toolbar lagging and wobbling behind the drag.
const isWindowResizing = ref(false)
const endWindowResize = useDebounceFn(() => { isWindowResizing.value = false }, 150)
useEventListener(hostWindow, 'resize', () => {
  isWindowResizing.value = true
  void endWindowResize()
})
const { isDragging, dropPreviewStyle, isOverBackground } = useToolbarDrag(toolbarEl, toolbarPosition, '.book-view')
const isVerticalToolbar = computed(
  () => toolbarPosition.value === 'left' || toolbarPosition.value === 'right',
)

const hasVersions = computed(() => props.versions.length > 0)
// Always present: even with no catalog suggestions the reader can add their own scans.
const hasHebrewBooksEditions = computed(() => true)

/** Whether the word-link markers control is on the toolbar - it tells us; see its own note. */
const wordLinkMarkersPresent = ref(false)

/** Whether the bookmarks control is on the toolbar - only once the book has a bookmark; it tells us. */
const bookmarksPresent = ref(false)

/**
 * Whether this book renders a control. Every condition here must match the
 * `v-if` it stands for - including the word-link markers dropdown's, which lives in that
 * component - or the toolbar counts room for a control that is not there, or misses one
 * that is and leaves it overflowing the pane.
 */
function isPresent(key: ToolbarOverflowKey): boolean {
  switch (key) {
    case 'hebrewbooks-editions': return hasHebrewBooksEditions.value
    case 'versions': return hasVersions.value
    // Reported by the control itself: it renders out of a list it fetches.
    case 'word-link-markers': return wordLinkMarkersPresent.value
    case 'bookmarks': return bookmarksPresent.value
    case 'read-aloud': return readAloudSupported
    case 'sync-commentaries': return props.hasCommentaries
    case 'related-books': return props.hasRelatedBooks
    case 'side-commentaries': return props.hasCommentaries && !!props.canUseSidePanel
    case 'bottom-commentary': return props.hasCommentaries
    default: return true
  }
}

const { hasToolbarOverflow, overflowGroupStarts, overflowedInToolbarOrder, toolbarButtonVisible } = useBookViewToolbarOverflow(
  toolbarEl,
  isPresent,
  isVerticalToolbar,
)

/** What a control on the toolbar wears so it can collapse into the flyout. */
function collapseAttrs(key: ToolbarOverflowKey) {
  const visible = toolbarButtonVisible(key)
  return { class: { collapsible: true, collapsed: !visible }, inert: !visible }
}

/** The book details dialog (opened from the "more" menu). */
const bookDetailsOpen = ref(false)
const overflowButtonEl = ref<HTMLElement | null>(null)

const more = useToolbarMoreMenu(toolbarPosition)
function openMore() {
  more.openFrom(overflowButtonEl.value)
}

/**
 * The "more" menu, as the PDF toolbar's: one row per collapsed control, in toolbar order, a
 * separator between groups; each row runs the same store action or emit its toolbar button does.
 * A dropdown control is a row with a submenu arrow (its own moreMenuRow); picking an item applies
 * it and closes the menu.
 */
const moreItems = computed<ContextMenuItem[]>(() => {
  const items: ContextMenuItem[] = []
  const hide = () => more.hide()
  const firstLine = (text: string) => text.split('\n')[0] ?? text
  for (const key of overflowedInToolbarOrder.value) {
    if (items.length && overflowGroupStarts.value.has(key)) items.push({ type: 'separator' })
    switch (key) {
      case 'toc':
        items.push({ type: 'checkbox', icon: IconTreeRtl, label: t('bookView.toolbar.toc'), checked: props.tocVisible, disabled: !props.hasToc, onChange: () => { hide(); emit('toggleToc') } })
        break
      case 'section-nav':
        items.push({ icon: IconChevronRight20Regular, label: t('bookView.toolbar.prevSection'), disabled: !props.hasToc, action: () => emit('navigateToPreviousSection'), keepOpen: true })
        items.push({ icon: IconChevronLeft20Regular, label: t('bookView.toolbar.nextSection'), disabled: !props.hasToc, action: () => emit('navigateToNextSection'), keepOpen: true })
        break
      case 'first-last':
        items.push({ icon: IconPdfFirstPage, label: t('bookView.toolbar.goToStart'), action: () => emit('goToStart') })
        items.push({ icon: IconPdfLastPage, label: t('bookView.toolbar.goToEnd'), action: () => emit('goToEnd') })
        break
      case 'bookmarks':
        if (bookmarksDropdownRef.value) items.push(bookmarksDropdownRef.value.moreMenuRow())
        break
      case 'search':
        items.push({ type: 'checkbox', icon: IconSearch20Regular, label: t('bookView.toolbar.search'), checked: props.searchVisible, onChange: () => { hide(); emit('toggleSearch') } })
        break
      case 'zoom':
        items.push({ type: 'heading', label: ZOOM_LABEL.value })
        items.push({ icon: IconZoomOut20Regular, label: firstLine(zoomOutTitle.value), title: zoomOutTitle.value, disabled: zoom.value <= ZOOM_CONFIG.MIN && commentaryZoom.value <= ZOOM_CONFIG.MIN, action: () => zoomStep('out'), keepOpen: true })
        items.push({ icon: IconZoomIn20Regular, label: firstLine(zoomInTitle.value), title: zoomInTitle.value, disabled: zoom.value >= ZOOM_CONFIG.MAX && commentaryZoom.value >= ZOOM_CONFIG.MAX, action: () => zoomStep('in'), keepOpen: true })
        break
      case 'clean-text':
        if (cleanTextRef.value) items.push(cleanTextRef.value.moreMenuRow())
        break
      case 'lines-layout':
        items.push({ type: 'checkbox', icon: flowText.value ? IconTextAlignJustify20Regular : IconTextAlignRight20Regular, label: flowTextLabel.value, checked: flowText.value, onChange: () => { toggleFlowText(); hide() } })
        break
      case 'side-commentaries':
        items.push({ type: 'checkbox', icon: props.sideCommentaryVisible ? IconLayoutColumnTwoFocusRight20Filled : IconLayoutColumnTwo20Regular, label: sideCommentaryTitle.value, checked: !!props.sideCommentaryVisible, onChange: () => { hide(); emit('toggleSideCommentary') } })
        items.push({ type: 'checkbox', icon: props.sideLeftCommentaryVisible ? IconLayoutColumnTwoFocusLeft20Filled : IconLayoutColumnTwo20Regular, label: sideLeftCommentaryTitle.value, checked: !!props.sideLeftCommentaryVisible, onChange: () => { hide(); emit('toggleSideLeftCommentary') } })
        break
      case 'bottom-commentary':
        items.push({ type: 'checkbox', icon: props.bottomCommentaryVisible ? IconLayoutRowTwoFocusBottom20Filled : IconLayoutRowTwo20Regular, label: bottomCommentaryTitle.value, checked: !!props.bottomCommentaryVisible, onChange: () => { hide(); emit('toggleBottomCommentary') } })
        break
      case 'sync-commentaries':
        items.push({ type: 'checkbox', icon: autoSelectTopLine.value ? IconTimeline20Filled : IconTimeline20Regular, label: SYNC_COMMENTARIES_LABEL, title: autoSelectTopLineTitle.value, checked: autoSelectTopLine.value, onChange: () => { bookViewStore.toggleAutoSelectTopLine(); hide() } })
        break
      case 'related-books':
        if (relatedBooksRef.value) items.push(relatedBooksRef.value.moreMenuRow())
        break
      case 'versions':
        if (versionsRef.value) items.push(versionsRef.value.moreMenuRow(hide))
        break
      case 'hebrewbooks-editions':
        if (hebrewBooksEditionsRef.value) items.push(hebrewBooksEditionsRef.value.moreMenuRow())
        break
      case 'word-link-markers':
        if (wordLinkMarkersRef.value) items.push(wordLinkMarkersRef.value.moreMenuRow())
        break
      case 'read-aloud':
        items.push({ type: 'checkbox', icon: props.readAloudOpen ? IconReadAloud20Filled : IconReadAloud20Regular, label: readAloudLabel.value, title: readAloudTitle.value, checked: !!props.readAloudOpen, onChange: () => { hide(); emit('toggleReadAloud') } })
        break
      case 'export-to-word':
        // A publisher-restricted book keeps the row: a click shows the reason (BookViewPage.onExportToWord).
        items.push(props.exportingToWord
          ? { icon: IconDismiss20Regular, label: t('bookView.toolbar.cancelExportToWord'), action: () => emit('cancelExportToWord') }
          : { icon: BookViewExportToWordGlyph, label: EXPORT_TO_WORD_LABEL, title: props.exportRestricted ? EXPORT_RESTRICTED_LABEL : undefined, action: () => emit('exportToWord') })
        break
    }
  }
  // Always here, never on the bar: what the library records about the book.
  if (items.length) items.push({ type: 'separator' })
  items.push({ icon: IconInfo20Regular, label: t('bookView.bookInfo.title'), disabled: props.bookId == null, action: () => { bookDetailsOpen.value = true } })
  return items
})

defineExpose({ tocBtnRef, createBookmark })
</script>

<template>
  <div ref="toolbarEl" class="book-view-toolbar reading-toolbar has-zones" dir="rtl" :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging, 'has-overflow': hasToolbarOverflow, 'window-resizing': isWindowResizing }]" :title="isOverBackground ? $t('bookView.toolbar.dragHint') : undefined">
    <Teleport :to="hostWindow.document.body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>
    <!-- Three zones (RTL), as the PDF toolbar: navigation at the start (right); search, display and
         commentary panels in the centre; the book's references and tools at the end (left). -->
    <div class="tb-zone tb-zone-start" data-toolbar-background>
    <button
      ref="tocBtnRef"
      v-bind="collapseAttrs('toc')"
      :class="{ active: tocVisible }"
      :disabled="!hasToc"
      :title="$t('bookView.toolbar.toc')"
      @click="$emit('toggleToc')"
    >
      <IconTreeRtl />
    </button>
    <button
      v-bind="collapseAttrs('section-nav')"
      :disabled="!hasToc"
      :title="$t('bookView.toolbar.prevSection')"
      @click="$emit('navigateToPreviousSection')"
    >
      <IconChevronRight20Regular class="dir-icon" />
    </button>
    <button
      v-bind="collapseAttrs('section-nav')"
      :disabled="!hasToc"
      :title="$t('bookView.toolbar.nextSection')"
      @click="$emit('navigateToNextSection')"
    >
      <IconChevronLeft20Regular class="dir-icon" />
    </button>
    <!-- first / last line of the book (the PDF toolbar's first / last page) -->
    <button
      v-bind="collapseAttrs('first-last')"
      :title="$t('bookView.toolbar.goToStart')"
      @click="$emit('goToStart')"
    >
      <IconPdfFirstPage />
    </button>
    <button
      v-bind="collapseAttrs('first-last')"
      :title="$t('bookView.toolbar.goToEnd')"
      @click="$emit('goToEnd')"
    >
      <IconPdfLastPage />
    </button>
    <BookViewBookmarksDropdown
      ref="bookmarksDropdownRef"
      v-bind="collapseAttrs('bookmarks')"
      class="dropdown-host"
      :book-id="bookId"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :label-for-line="bookmarkLabelForLine"
      :read-bookmark-point="readBookmarkPoint"
      @presence-change="bookmarksPresent = $event"
      @go-to-bookmark="$emit('goToBookmark', $event)"
    />
    <BookViewRelatedBooksDropdown
      v-if="hasRelatedBooks"
      ref="relatedBooksRef"
      v-bind="collapseAttrs('related-books')"
      class="dropdown-host"
      :book-id="bookId"
      :current-scroll-line-index="currentScrollLineIndex"
    />
    <!--
      Only for the minority of books that carry alternate versions. Absent rather
      than disabled: a book with one text has no choice to offer.
    -->
    <BookViewVersionsDropdown
      v-if="hasVersions"
      ref="versionsRef"
      v-bind="collapseAttrs('versions')"
      class="dropdown-host"
      :versions="versions"
      :active-version-id="activeVersionId"
      @select="$emit('selectVersion', $event)"
    />
    <!-- Scanned editions: catalog suggestions, the reader's own items, and adding more. -->
    <BookViewHebrewBooksEditionsDropdown
      v-if="hasHebrewBooksEditions"
      ref="hebrewBooksEditionsRef"
      v-bind="collapseAttrs('hebrewbooks-editions')"
      class="dropdown-host"
      :editions="hebrewBooksEditions"
      :talmud-pdf-path="hebrewBooksTalmudPdfPath"
    />
    </div>
    <div class="tb-zone tb-zone-center" data-toolbar-background>
    <button
      v-bind="collapseAttrs('search')"
      :class="{ active: searchVisible }"
      :title="$t('bookView.toolbar.search')"
      @click="$emit('toggleSearch')"
    >
      <IconSearch20Regular />
    </button>

    <button
      v-bind="collapseAttrs('zoom')"
      :title="zoomOutTitle"
      :disabled="zoom <= ZOOM_CONFIG.MIN && commentaryZoom <= ZOOM_CONFIG.MIN"
      @pointerdown="startContinuousZoom('out')"
      @pointerup="stopContinuousZoom"
      @pointerleave="stopContinuousZoom"
      @pointercancel="stopContinuousZoom"
    >
      <IconZoomOut20Regular />
    </button>
    <button
      v-bind="collapseAttrs('zoom')"
      :title="zoomInTitle"
      :disabled="zoom >= ZOOM_CONFIG.MAX && commentaryZoom >= ZOOM_CONFIG.MAX"
      @pointerdown="startContinuousZoom('in')"
      @pointerup="stopContinuousZoom"
      @pointerleave="stopContinuousZoom"
      @pointercancel="stopContinuousZoom"
    >
      <IconZoomIn20Regular />
    </button>

    <!--
      RTL: first child sits physically right. The three buttons run right → bottom
      → left, so each sits on the side of the panel it controls. Both side panels
      need a wide pane, so their buttons are absent on a narrow one. With no
      commentaries at all there is nothing any of them could open, so they drop
      out of the toolbar rather than sitting there greyed out.
    -->
    <button
      v-if="canUseSidePanel && hasCommentaries"
      v-bind="collapseAttrs('side-commentaries')"
      :class="{ active: sideCommentaryVisible }"
      :title="sideCommentaryTitle"
      @click="$emit('toggleSideCommentary')"
    >
      <IconLayoutColumnTwoFocusRight20Filled v-if="sideCommentaryVisible" />
      <IconLayoutColumnTwo20Regular v-else />
    </button>
    <button
      v-if="hasCommentaries"
      v-bind="collapseAttrs('bottom-commentary')"
      :class="{ active: bottomCommentaryVisible }"
      :title="bottomCommentaryTitle"
      @click="$emit('toggleBottomCommentary')"
    >
      <IconLayoutRowTwoFocusBottom20Filled v-if="bottomCommentaryVisible" />
      <IconLayoutRowTwo20Regular v-else />
    </button>
    <button
      v-if="canUseSidePanel && hasCommentaries"
      v-bind="collapseAttrs('side-commentaries')"
      :class="{ active: sideLeftCommentaryVisible }"
      :title="sideLeftCommentaryTitle"
      @click="$emit('toggleSideLeftCommentary')"
    >
      <IconLayoutColumnTwoFocusLeft20Filled v-if="sideLeftCommentaryVisible" />
      <IconLayoutColumnTwo20Regular v-else />
    </button>
    <!--
      Hidden with the panel toggles above, though it is one app-wide persisted
      setting rather than a panel: it only ever syncs the commentary panel, so a
      book with no commentaries has nothing for it to act on. Its default still
      lives in the app settings, so it is not stranded while a plain book is open.
    -->
    <button
      v-if="hasCommentaries"
      v-bind="collapseAttrs('sync-commentaries')"
      :class="{ active: autoSelectTopLine }"
      :title="autoSelectTopLineTitle"
      @click="bookViewStore.toggleAutoSelectTopLine()"
    >
      <IconTimeline20Filled v-if="autoSelectTopLine" />
      <IconTimeline20Regular v-else />
    </button>

    </div>
    <div class="tb-zone tb-zone-end" data-toolbar-background>
    <BookViewCleanTextCombo ref="cleanTextRef" v-bind="collapseAttrs('clean-text')" class="dropdown-host" />

    <button
      v-bind="collapseAttrs('lines-layout')"
      :class="{ active: flowText }"
      :title="flowTextLabel"
      @click="toggleFlowText"
    >
      <IconTextAlignJustify20Regular v-if="flowText" />
      <IconTextAlignRight20Regular v-else />
    </button>
    <BookViewWordLinkMarkersDropdown
      ref="wordLinkMarkersRef"
      v-bind="collapseAttrs('word-link-markers')"
      class="dropdown-host"
      :book-id="bookId"
      @presence-change="wordLinkMarkersPresent = $event"
    />
    <button
      v-if="readAloudSupported"
      v-bind="collapseAttrs('read-aloud')"
      :class="{ active: readAloudOpen }"
      :title="readAloudTitle"
      @click="$emit('toggleReadAloud')"
    >
      <IconReadAloud20Filled v-if="readAloudOpen" />
      <IconReadAloud20Regular v-else />
    </button>
    <!-- The shared conversion button: while the export runs it is an animated X that cancels it. -->
    <!-- A publisher-restricted book keeps the button, dimmed, so its tooltip says why;
         a click shows the same message (BookViewPage.onExportToWord) and exports nothing. -->
    <ConversionActionButton
      v-bind="collapseAttrs('export-to-word')"
      :class="{ 'publisher-restricted': exportRestricted }"
      :aria-disabled="exportRestricted || undefined"
      :state="exportingToWord ? 'busy' : 'idle'"
      :idle-title="exportRestricted ? EXPORT_RESTRICTED_LABEL : EXPORT_TO_WORD_LABEL"
      :busy-title="t('bookView.toolbar.cancelExportToWord')"
      @start="$emit('exportToWord')"
      @cancel="$emit('cancelExportToWord')"
    >
      <BookViewExportToWordGlyph />
    </ConversionActionButton>
    </div>
    <!--
      Stands in for whatever the pane was too narrow to hold. It holds ONE place, at the
      toolbar's far end, and holds it at every width: it is out of the flex flow entirely
      (see .overflow-btn) and the row of buttons ends before it rather than pushing it
      along. Buttons collapse INTO it as the pane narrows; it does not move as they go.

      Always rendered, never conditional, for the same reason - a button that appeared only
      once something had collapsed would be a control materialising under the pointer. It is
      always enabled: book info lives only in its flyout.
    -->
    <button
      ref="overflowButtonEl"
      class="overflow-btn"
      :class="{ 'has-collapsed': hasToolbarOverflow }"
      :title="MORE_LABEL"
      aria-haspopup="menu"
      @click="openMore"
    >
      <IconMoreHorizontal24Regular />
    </button>
    <!-- The toolbar's "more" menu: the PDF toolbar's ContextMenu, items built from data (moreItems). -->
    <ContextMenu :ref="more.menuRef" :items="moreItems" drill-in />
    <BookViewBookDetailsDialog v-if="bookDetailsOpen" :book-id="bookId" @close="bookDetailsOpen = false" />
  </div>
</template>

<style scoped>
/* The shell, orientations, edge line and drag preview: assets/styles/readingToolbar.css (.reading-toolbar). */
/* ── Buttons ── */
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

/* ── Collapsing into the overflow ─────────────────────────────────────────────
   A control that no longer fits is NOT unmounted. Removing it would take its width out of
   the row in a single frame, and because the row is centred every other button jumps half
   that width at the same moment - two jolts from one change, on a toolbar the user is
   actively dragging the edge of.

   So it stays in the flow and collapses to zero width and padding, at once - no animation. */
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
/* A vertical toolbar collapses along its own axis - height, not width. */
.toolbar-left .collapsible.collapsed,
.toolbar-right .collapsible.collapsed {
  height: 0;
  margin-block: 0;
  padding-block: 0;
  opacity: 0;
}
/* Belt and braces alongside the `inert` attribute the collapsed controls carry: `inert` is
   what actually takes them out of the tab order, out of the accessibility tree and out of
   the way of clicks, and this covers a browser that has the property but not the attribute. */
.collapsible.collapsed {
  pointer-events: none;
}


/* Anchored, not laid out. The one control that must never move: buttons collapse into it as
   the pane narrows, and a target that slid along the bar as they went - or appeared out of
   nowhere once the first one did - would be moving at exactly the moment someone reaches
   for it.

   So it is taken out of the flex flow with `position: absolute` and pinned to a PHYSICAL
   edge of the toolbar, and the toolbar reserves its width in padding on that side (see
   .book-view-toolbar) so the row of buttons ends before it instead of running under it.
   Physical `left`/`bottom` deliberately, not the logical properties: the document is RTL,
   where the logical end edge maps to the physical right - the wrong side. */
.overflow-btn {
  position: absolute;
  inset-inline-end: var(--toolbar-edge-inset);
  top: 50%;
  transform: translateY(-50%);
  /* Painted on the toolbar's own surface rather than over a transparent hole: it sits ABOVE
     the button row, and a pane narrow enough to push a button under it would otherwise show
     that button through the gap between this one's glyph and its edges. */
  background: var(--bg-toolbar);
  z-index: 1;
  /* Restates the background and colour fade from main.css's global `button` rule, because
     naming any property here replaces that declaration wholesale rather than adding to it. */
  transition:
    background 120ms,
    color 120ms;
}
/* The hover tint is a wash over the toolbar's surface, so it has to be composited onto that
   surface rather than replacing it - the global `button:hover` background would otherwise
   drop the opaque fill this button needs. */
.overflow-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}
@media (prefers-reduced-motion: reduce) {
  .overflow-btn {
    transition: none;
  }
}
/* A vertical toolbar runs as a column, so the same anchor is its bottom edge and the
   centring is horizontal. */
.toolbar-left .overflow-btn,
.toolbar-right .overflow-btn {
  left: 50%;
  top: auto;
  bottom: var(--toolbar-edge-inset);
  transform: translateX(-50%);
}


/* The dropdowns hang out of their boxes, so their clipping is lifted while they are on the
   bar; collapsing them still clips, as .collapsible intends. */
.collapsible.dropdown-host:not(.collapsed) {
  overflow: visible;
}


/* Export to Word on a publisher-restricted book: present (its tooltip explains) but dimmed. */
.publisher-restricted {
  opacity: 0.45;
}
</style>
