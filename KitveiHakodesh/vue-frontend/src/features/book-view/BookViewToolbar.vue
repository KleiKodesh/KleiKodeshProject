<script setup lang="ts">
import { computed, ref, watch, nextTick, useId } from 'vue'
import { useDebounceFn, useEventListener } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import {
  IconSearch20Regular,
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
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import BookViewRelatedBooksDropdown from './BookViewRelatedBooksDropdown.vue'
import BookViewBookmarksDropdown from './BookViewBookmarksDropdown.vue'
import type { BookBookmark } from '@/stores/bookBookmarks'
import type { BookViewLinesBookmarkPoint } from './lines/useBookViewLinesBookmarkPoint'
import BookViewVersionsDropdown from './BookViewVersionsDropdown.vue'
import BookViewHebrewBooksEditionsDropdown from './BookViewHebrewBooksEditionsDropdown.vue'
import BookViewBookInfoDropdown from './BookViewBookInfoDropdown.vue'
import BookViewWordLinkMarkersDropdown from './BookViewWordLinkMarkersDropdown.vue'
import BookViewCleanTextCombo from './BookViewCleanTextCombo.vue'
import BookViewExportToWordGlyph from '@/components/WordDocumentGlyph.vue'
import { usePressRepeat } from '@/composables/usePressRepeat'
import ConversionActionButton from '@/components/ConversionActionButton.vue'
import BookViewToolbarOverflowMenu from './BookViewToolbarOverflowMenu.vue'
import {
  useBookViewToolbarOverflow,
  TOOLBAR_OVERFLOW_ORDER,
  type ToolbarOverflowKey,
} from './useBookViewToolbarOverflow'
import { useToolbarDrag } from '@/composables/useToolbarDrag'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { LinesTable } from './lines/linesTable'
import type { BookVersionRow, HebrewBooksEdition } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  searchVisible: boolean
  tocVisible: boolean
  hasToc: boolean
  hasCommentaries: boolean
  hasRelatedBooks: boolean
  // This pane's active tab — zoom actions and the displayed percentages are
  // scoped to it, so pane 2's toolbar never reads or writes pane 1's zoom.
  tabId?: string
  bookId: number | undefined
  // Set for books whose publisher forbids an editable export; the button is not rendered.
  exportRestricted?: boolean
  // True while an export to Word runs; the button shows a spinner.
  exportingToWord?: boolean
  filterGroups: CommentaryGroup[]
  relatedBooksLoaded: boolean
  // The book's alternate versions. Empty for most books, which hides the control.
  versions: BookVersionRow[]
  activeVersionId: number | null
  // The book's scanned editions on HebrewBooks. Empty for most books, which hides the control.
  hebrewBooksEditions: HebrewBooksEdition[]
  currentScrollLineIndex: number
  lines: LinesTable
  onRelatedBooksOpen?: () => void
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
}>()
defineEmits<{ toggleBottomCommentary: []; toggleSideCommentary: []; toggleSideLeftCommentary: []; toggleSearch: []; toggleToc: []; exportToWord: []; cancelExportToWord: []; navigateToNextSection: []; navigateToPreviousSection: []; selectVersion: [versionId: number | null]; goToBookmark: [bookmark: BookBookmark] }>()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom, commentaryZoom, toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

/** The export control's name - its tooltip on the toolbar, its text in the flyout. */
const EXPORT_TO_WORD_LABEL = t('bookView.toolbar.exportToWord')

/** The overflow button's name - the same word the nav rail's overflow button wears. */
const MORE_LABEL = t('bookView.toolbar.more')

/**
 * The name the sync-commentaries row shows. It is the first line of that control's own
 * tooltip - the tooltip carries the name followed by an explanation, and a row wants just
 * the name.
 */
const SYNC_COMMENTARIES_LABEL = t('bookView.toolbar.syncCommentaries')

const tocBtnRef = ref<HTMLElement | null>(null)
const bookmarksDropdownRef = ref<InstanceType<typeof BookViewBookmarksDropdown> | null>(null)
const overflowBookmarksDropdownRef = ref<InstanceType<typeof BookViewBookmarksDropdown> | null>(null)

/**
 * Ctrl+Shift+D and the lines' context menu: opens the bookmarks dropdown on a new bookmark,
 * its name field focused. With bookmarks collapsed, that is the flyout's bookmarks row and
 * its submenu.
 *
 * The point is read first, before the flyout opens and moves focus off the selection. Then
 * the control is counted as present BEFORE the toolbar decides where it goes: on a book's
 * first bookmark it is not on the toolbar yet, and opening it there would add it to the
 * count only after the choice was made - on a narrow pane it then collapses in the same
 * tick, taking the panel and its name field with it, invisible and inert.
 */
async function createBookmark() {
  const point = props.readBookmarkPoint?.() ?? null
  bookmarksPresent.value = true
  await nextTick()
  if (toolbarButtonVisible('bookmarks')) {
    void bookmarksDropdownRef.value?.createBookmark(point)
    return
  }
  overflowOpen.value = true
  await nextTick()
  void overflowBookmarksDropdownRef.value?.createBookmark(point)
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

// Pane-scoped zoom percentages — the store's `zoom`/`commentaryZoom` computeds
// always reflect pane 1's active tab, so read this tab's values directly.
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

const sideCommentaryTitle = computed(() =>
  props.sideCommentaryVisible
    ? t('bookView.toolbar.close_right')
    : t('bookView.toolbar.open_right'),
)

const sideLeftCommentaryTitle = computed(() =>
  props.sideLeftCommentaryVisible
    ? t('bookView.toolbar.close_left')
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
useEventListener(window, 'resize', () => {
  isWindowResizing.value = true
  void endWindowResize()
})
const { isDragging, dropPreviewStyle } = useToolbarDrag(toolbarEl, toolbarPosition, '.book-view')
const isVerticalToolbar = computed(
  () => toolbarPosition.value === 'left' || toolbarPosition.value === 'right',
)

const hasVersions = computed(() => props.versions.length > 0)
const hasHebrewBooksEditions = computed(() => props.hebrewBooksEditions.length > 0)

/** Whether the word-link markers control is on the toolbar - it tells us; see its own note. */
const wordLinkMarkersPresent = ref(false)

/** Whether the bookmarks control is on the toolbar - only once the book has a bookmark; it tells us. */
const bookmarksPresent = ref(false)

/**
 * The controls this book renders, in collapse order. Every condition here must match the
 * `v-if` it stands for - including the word-link markers dropdown's, which lives in that
 * component - or the toolbar counts room for a control that is not there, or misses one
 * that is and leaves it overflowing the pane.
 */
const presentOverflowKeys = computed<ToolbarOverflowKey[]>(() =>
  TOOLBAR_OVERFLOW_ORDER.filter((key) => {
    switch (key) {
      case 'hebrewbooks-editions': return hasHebrewBooksEditions.value
      case 'versions': return hasVersions.value
      // Reported by the control itself: it renders out of a list it fetches.
      case 'word-link-markers': return wordLinkMarkersPresent.value
      case 'bookmarks': return bookmarksPresent.value
      case 'export-to-word': return !props.exportRestricted
      case 'sync-commentaries': return props.hasCommentaries
      case 'related-books': return props.hasRelatedBooks
      case 'side-commentaries': return props.hasCommentaries && !!props.canUseSidePanel
      case 'bottom-commentary': return props.hasCommentaries
      default: return true
    }
  }),
)

const { hasToolbarOverflow, overflowedKeys, toolbarButtonVisible } = useBookViewToolbarOverflow(
  toolbarEl,
  presentOverflowKeys,
  isVerticalToolbar,
)

/** What a control on the toolbar wears so it can collapse into the flyout. */
function collapseAttrs(key: ToolbarOverflowKey) {
  const visible = toolbarButtonVisible(key)
  return { class: { collapsible: true, collapsed: !visible }, inert: !visible }
}

const overflowOpen = ref(false)
const overflowButtonEl = ref<HTMLElement | null>(null)

// Both toolbars in a split view render this row, so the id has to be unique per instance or
// the second one's group points at the first one's label.
const zoomRowLabelId = `bv-zoom-row-${useId()}`

function isOverflowed(key: ToolbarOverflowKey) {
  return overflowedKeys.value.includes(key)
}

// A resize that gives the room back hides the more button - its flyout must not be left
// floating beside a button nobody can see.
watch(hasToolbarOverflow, (has) => {
  if (!has) overflowOpen.value = false
})

defineExpose({ tocBtnRef, createBookmark })
</script>

<template>
  <div ref="toolbarEl" class="book-view-toolbar reading-toolbar" dir="rtl" :class="[`toolbar-${toolbarPosition}`, { dragging: isDragging, 'window-resizing': isWindowResizing }]" :title="$t('bookView.toolbar.dragHint')">
    <Teleport to="body">
      <div v-if="isDragging" class="toolbar-drop-preview" :style="dropPreviewStyle" />
    </Teleport>
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
      v-bind="collapseAttrs('related-books')"
      class="dropdown-host"
      :book-id="bookId"
      :filter-groups="filterGroups"
      :related-books-loaded="relatedBooksLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :on-open="onRelatedBooksOpen"
    />
    <!--
      Only for the minority of books that carry alternate versions. Absent rather
      than disabled: a book with one text has no choice to offer.
    -->
    <BookViewVersionsDropdown
      v-if="hasVersions"
      v-bind="collapseAttrs('versions')"
      class="dropdown-host"
      :versions="versions"
      :active-version-id="activeVersionId"
      @select="$emit('selectVersion', $event)"
    />
    <!-- Only for books with scanned editions on HebrewBooks; absent otherwise, like versions. -->
    <BookViewHebrewBooksEditionsDropdown
      v-if="hasHebrewBooksEditions"
      v-bind="collapseAttrs('hebrewbooks-editions')"
      class="dropdown-host"
      :editions="hebrewBooksEditions"
    />
    <BookViewWordLinkMarkersDropdown
      v-bind="collapseAttrs('word-link-markers')"
      class="dropdown-host"
      :book-id="bookId"
      @presence-change="wordLinkMarkersPresent = $event"
    />
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
    <button
      v-bind="collapseAttrs('search')"
      :class="{ active: searchVisible }"
      :title="$t('bookView.toolbar.search')"
      @click="$emit('toggleSearch')"
    >
      <IconSearch20Regular />
    </button>
    <BookViewBookInfoDropdown
      v-bind="collapseAttrs('book-info')"
      class="dropdown-host"
      :is-collapsed="!toolbarButtonVisible('book-info')"
      :book-id="bookId"
    />

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

    <BookViewCleanTextCombo v-bind="collapseAttrs('clean-text')" class="dropdown-host" />

    <!-- The shared conversion button: while the export runs it is an animated X that cancels it. -->
    <ConversionActionButton
      v-if="!exportRestricted"
      v-bind="collapseAttrs('export-to-word')"
      :state="exportingToWord ? 'busy' : 'idle'"
      :idle-title="EXPORT_TO_WORD_LABEL"
      :busy-title="t('bookView.toolbar.cancelExportToWord')"
      @start="$emit('exportToWord')"
      @cancel="$emit('cancelExportToWord')"
    >
      <BookViewExportToWordGlyph />
    </ConversionActionButton>
    <!--
      Stands in for whatever the pane was too narrow to hold. It holds ONE place, at the
      toolbar's far end, and holds it at every width: it is out of the flex flow entirely
      (see .overflow-btn) and the row of buttons ends before it rather than pushing it
      along. Buttons collapse INTO it as the pane narrows; it does not move as they go.

      Always rendered, never conditional, for the same reason - a button that appeared only
      once something had collapsed would be a control materialising under the pointer. When
      nothing has collapsed it is simply disabled, so its place is visibly reserved.
    -->
    <button
      ref="overflowButtonEl"
      :disabled="!hasToolbarOverflow"
      class="overflow-btn"
      :class="{ active: overflowOpen }"
      :title="MORE_LABEL"
      aria-haspopup="menu"
      :aria-expanded="overflowOpen"
      @click="overflowOpen = !overflowOpen"
    >
      <IconMoreHorizontal24Regular />
    </button>
    <!--
      One row per collapsed control, in the collapse order, so the first control to go is
      the first row. Each row drives the same store or emit its toolbar button does - the
      flyout renders a second face for a control, never a second implementation of it.
    -->
    <BookViewToolbarOverflowMenu
      v-model:open="overflowOpen"
      :anchor="overflowButtonEl"
      :keep-clear-of="toolbarEl"
      :toolbar-position="toolbarPosition"
    >
      <!--
        Rows in collapse order. Each dropdown renders a second instance of itself as a row
        whose list opens as a side submenu, like a context menu's. Rows that finish when
        picked (export, search, TOC) dismiss the flyout; toggles and steps you repeat do not.
      -->
      <BookViewBookInfoDropdown v-if="isOverflowed('book-info')" in-overflow :book-id="bookId" />
      <BookViewHebrewBooksEditionsDropdown
        v-if="isOverflowed('hebrewbooks-editions')"
        in-overflow
        :editions="hebrewBooksEditions"
      />
      <BookViewVersionsDropdown
        v-if="isOverflowed('versions')"
        in-overflow
        :versions="versions"
        :active-version-id="activeVersionId"
        @select="$emit('selectVersion', $event)"
      />
      <BookViewWordLinkMarkersDropdown v-if="isOverflowed('word-link-markers')" in-overflow :book-id="bookId" />
      <ConversionActionButton
        v-if="isOverflowed('export-to-word')"
        role="menuitem"
        class="overflow-row"
        :state="exportingToWord ? 'busy' : 'idle'"
        :idle-title="EXPORT_TO_WORD_LABEL"
        :busy-title="t('bookView.toolbar.cancelExportToWord')"
        :label="EXPORT_TO_WORD_LABEL"
        @start="overflowOpen = false; $emit('exportToWord')"
        @cancel="overflowOpen = false; $emit('cancelExportToWord')"
      >
        <BookViewExportToWordGlyph />
      </ConversionActionButton>
      <button
        v-if="isOverflowed('sync-commentaries')"
        role="menuitem"
        class="overflow-row"
        :class="{ active: autoSelectTopLine }"
        :title="autoSelectTopLineTitle"
        @click="bookViewStore.toggleAutoSelectTopLine()"
      >
        <IconTimeline20Filled v-if="autoSelectTopLine" />
        <IconTimeline20Regular v-else />
        <span>{{ SYNC_COMMENTARIES_LABEL }}</span>
      </button>
      <BookViewRelatedBooksDropdown
        v-if="isOverflowed('related-books')"
        in-overflow
        :book-id="bookId"
        :filter-groups="filterGroups"
        :related-books-loaded="relatedBooksLoaded"
        :current-scroll-line-index="currentScrollLineIndex"
        :lines="lines"
        :on-open="onRelatedBooksOpen"
      />
      <BookViewCleanTextCombo v-if="isOverflowed('clean-text')" in-overflow />
      <!--
        The zoom pair as ONE row: two buttons on the toolbar, but a row is a full-width
        strip and there is no reason to spend two of them on a control whose two halves are
        the same action in opposite directions. Both sit at the row's end, past the label,
        and neither closes the flyout - zooming is something you do repeatedly and watch,
        so the menu stays up until you dismiss it.
      -->
      <div
        v-if="isOverflowed('zoom')"
        role="group"
        :aria-labelledby="zoomRowLabelId"
        class="overflow-row overflow-row-zoom"
      >
        <!-- The group is named BY this text rather than by a copy of it in an aria-label,
             or the reading is the percentage twice over. -->
        <span :id="zoomRowLabelId">{{ ZOOM_LABEL }}</span>
        <button
          role="menuitem"
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
          role="menuitem"
          :title="zoomInTitle"
          :disabled="zoom >= ZOOM_CONFIG.MAX && commentaryZoom >= ZOOM_CONFIG.MAX"
          @pointerdown="startContinuousZoom('in')"
          @pointerup="stopContinuousZoom"
          @pointerleave="stopContinuousZoom"
          @pointercancel="stopContinuousZoom"
        >
          <IconZoomIn20Regular />
        </button>
      </div>
      <template v-if="isOverflowed('side-commentaries')">
        <button
          role="menuitem"
          class="overflow-row"
          :class="{ active: sideCommentaryVisible }"
          @click="$emit('toggleSideCommentary')"
        >
          <IconLayoutColumnTwoFocusRight20Filled v-if="sideCommentaryVisible" />
          <IconLayoutColumnTwo20Regular v-else />
          <span>{{ sideCommentaryTitle }}</span>
        </button>
        <button
          role="menuitem"
          class="overflow-row"
          :class="{ active: sideLeftCommentaryVisible }"
          @click="$emit('toggleSideLeftCommentary')"
        >
          <IconLayoutColumnTwoFocusLeft20Filled v-if="sideLeftCommentaryVisible" />
          <IconLayoutColumnTwo20Regular v-else />
          <span>{{ sideLeftCommentaryTitle }}</span>
        </button>
      </template>
      <button
        v-if="isOverflowed('bottom-commentary')"
        role="menuitem"
        class="overflow-row"
        :class="{ active: bottomCommentaryVisible }"
        @click="$emit('toggleBottomCommentary')"
      >
        <IconLayoutRowTwoFocusBottom20Filled v-if="bottomCommentaryVisible" />
        <IconLayoutRowTwo20Regular v-else />
        <span>{{ bottomCommentaryTitle }}</span>
      </button>
      <BookViewBookmarksDropdown
        v-if="isOverflowed('bookmarks')"
        ref="overflowBookmarksDropdownRef"
        in-overflow
        :book-id="bookId"
        :current-scroll-line-index="currentScrollLineIndex"
        :lines="lines"
        :label-for-line="bookmarkLabelForLine"
        :read-bookmark-point="readBookmarkPoint"
        @go-to-bookmark="overflowOpen = false; $emit('goToBookmark', $event)"
      />
      <button
        v-if="isOverflowed('search')"
        role="menuitem"
        class="overflow-row"
        :class="{ active: searchVisible }"
        @click="overflowOpen = false; $emit('toggleSearch')"
      >
        <IconSearch20Regular />
        <span>{{ $t('bookView.toolbar.search') }}</span>
      </button>
      <template v-if="isOverflowed('section-nav')">
        <button
          role="menuitem"
          class="overflow-row"
          :disabled="!hasToc"
          @click="$emit('navigateToPreviousSection')"
        >
          <IconChevronRight20Regular class="dir-icon" />
          <span>{{ $t('bookView.toolbar.prevSection') }}</span>
        </button>
        <button
          role="menuitem"
          class="overflow-row"
          :disabled="!hasToc"
          @click="$emit('navigateToNextSection')"
        >
          <IconChevronLeft20Regular class="dir-icon" />
          <span>{{ $t('bookView.toolbar.nextSection') }}</span>
        </button>
      </template>
      <button
        v-if="isOverflowed('toc')"
        role="menuitem"
        class="overflow-row"
        :class="{ active: tocVisible }"
        :disabled="!hasToc"
        @click="overflowOpen = false; $emit('toggleToc')"
      >
        <IconTreeRtl />
        <span>{{ $t('bookView.toolbar.toc') }}</span>
      </button>
    </BookViewToolbarOverflowMenu>
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
/* Nothing has collapsed: the button fades out and goes inert, but its place is still
   reserved in the toolbar's padding, so the first collapse fades it back in exactly where it
   will stay rather than shifting the row to make room.

   Opacity and `visibility` together, never `display: none`: the element has to keep its box
   - it is what the flyout anchors to - and visibility is what stops a fully transparent
   button from still taking clicks. Transitioning visibility alongside opacity holds it
   visible for the length of the fade and switches it at the far end, so the fade is seen in
   both directions instead of being cut short on the way out. */
.overflow-btn:disabled {
  opacity: 0;
  visibility: hidden;
  /* Not the `not-allowed` a disabled button normally shows: there is nothing here to refuse,
     only a reserved space that happens to be empty. */
  cursor: default;
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

</style>
