<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount, useId } from 'vue'
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
import BookViewVersionsDropdown from './BookViewVersionsDropdown.vue'
import BookViewWordLinkMarkersDropdown from './BookViewWordLinkMarkersDropdown.vue'
import BookViewDiacriticsGlyph from './BookViewDiacriticsGlyph.vue'
import BookViewExportToWordGlyph from './BookViewExportToWordGlyph.vue'
import BookViewToolbarOverflowMenu from './BookViewToolbarOverflowMenu.vue'
import {
  useBookViewToolbarOverflow,
  TOOLBAR_OVERFLOW_ORDER,
  TOOLBAR_SEPARATOR_IN_BUTTONS,
  type ToolbarOverflowKey,
} from './useBookViewToolbarOverflow'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import { COMMENTARY_SLOTS } from './bookViewTypes'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { LineItem } from './lines/useBookViewLinesTable'
import type { BookVersionRow } from '@/webview-host/queries.types'

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
  bookHasTeamim: boolean
  filterGroups: CommentaryGroup[]
  relatedBooksLoaded: boolean
  // The book's alternate versions. Empty for most books, which hides the control.
  versions: BookVersionRow[]
  activeVersionId: number | null
  currentScrollLineIndex: number
  lines: LineItem[]
  onRelatedBooksOpen?: () => void
  bottomCommentaryVisible?: boolean
  sideCommentaryVisible?: boolean
  sideLeftCommentaryVisible?: boolean
  // The side panels need a pane wide enough to hold them; on a narrow pane their
  // buttons are hidden entirely rather than offering a mode that cannot apply.
  canUseSidePanel?: boolean
}>()
defineEmits<{ toggleBottomCommentary: []; toggleSideCommentary: []; toggleSideLeftCommentary: []; toggleSearch: []; toggleToc: []; exportToWord: []; navigateToNextSection: []; navigateToPreviousSection: []; selectVersion: [versionId: number | null] }>()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom, commentaryZoom, toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

const diacriticsState = computed(() => settingsStore.diacriticsState)

// When the book has no teamim the cycle is 0→2→0, so the title reflects only two stages.
const diacriticsTitle = computed(() => {
  if (!props.bookHasTeamim) {
    return diacriticsState.value === 0 ? 'הסר ניקוד' : 'שחזר ניקוד'
  }
  return ['הסר טעמים', 'הסר גם ניקוד', 'שחזר טעמים וניקוד'][diacriticsState.value]!
})

function onDiacriticsClick() {
  if (props.bookHasTeamim) {
    settingsStore.cycleDiacritics()
  } else {
    settingsStore.cycleDiacriticsNoTeamim()
  }
}

/** The export control's name - its tooltip on the toolbar, its text in the flyout. */
const EXPORT_TO_WORD_LABEL = 'ייצא ל-Word'

/** The overflow button's name - the same word the nav rail's overflow button wears. */
const MORE_LABEL = 'פריטים נוספים'

/**
 * The name the sync-commentaries row shows. It is the first line of that control's own
 * tooltip - the tooltip carries the name followed by an explanation, and a row wants just
 * the name.
 */
const SYNC_COMMENTARIES_LABEL = 'סנכרן מפרשים'

// The diacritics control has no fixed name: its tooltip names the NEXT stage of the cycle,
// which is what a row should say too, so the row reads the same text the button's tooltip
// does.
const diacriticsRowLabel = computed(() => diacriticsTitle.value)

const tocBtnRef = ref<HTMLElement | null>(null)

// Continuous zoom — fires immediately on pointerdown, then waits 400ms before
// repeating every 80ms. The delay prevents a normal tap from triggering more
// than one step.
let zoomInterval: ReturnType<typeof setInterval> | null = null
let zoomDelayTimeout: ReturnType<typeof setTimeout> | null = null

function zoomStep(direction: 'in' | 'out') {
  if (direction === 'in') bookViewStore.zoomIn(props.tabId, props.bookId)
  else bookViewStore.zoomOut(props.tabId, props.bookId)
}

function startContinuousZoom(direction: 'in' | 'out') {
  if (zoomInterval !== null || zoomDelayTimeout !== null) return
  zoomStep(direction)
  zoomDelayTimeout = setTimeout(() => {
    zoomDelayTimeout = null
    zoomInterval = setInterval(() => zoomStep(direction), 80)
  }, 400)
}

function stopContinuousZoom() {
  if (zoomDelayTimeout !== null) {
    clearTimeout(zoomDelayTimeout)
    zoomDelayTimeout = null
  }
  if (zoomInterval !== null) {
    clearInterval(zoomInterval)
    zoomInterval = null
  }
}

// Only pointerup/leave/cancel stopped these, and none of them fire when the button
// unmounts under the pointer — closing the tab while holding zoom left an 80ms interval
// calling zoomIn/zoomOut on a dead tab for the life of the page.
onBeforeUnmount(stopContinuousZoom)

const autoSelectTopLineTitle = computed(() =>
  autoSelectTopLine.value
    ? 'סנכרן מפרשים\nלחץ לכיבוי הסנכרון האוטומטי'
    : 'סנכרן מפרשים\nמפרשים יתעדכנו אוטומטית לפי השורה העליונה',
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
  () => `הקטן (Ctrl-)\nטקסט: ${Math.round(linesZoomPct.value)}% | מפרשים: ${commentaryZoomLabel.value}\nאיפוס: Ctrl+0`,
)

const zoomInTitle = computed(
  () => `הגדל (Ctrl+)\nטקסט: ${Math.round(linesZoomPct.value)}% | מפרשים: ${commentaryZoomLabel.value}\nאיפוס: Ctrl+0`,
)

// The panels are independent, so each title names its own panel in both states —
// a shared "close the commentary panel" phrase left them indistinguishable. The
// two side panels are named by their edge, not just "בצד".
const bottomCommentaryTitle = computed(() =>
  props.bottomCommentaryVisible
    ? 'סגור חלונית מפרשים תחתונה (Ctrl+J)'
    : 'חלונית מפרשים תחתונה (Ctrl+J)',
)

const sideCommentaryTitle = computed(() =>
  props.sideCommentaryVisible
    ? 'סגור חלונית מפרשים מימין (Ctrl+Shift+J)'
    : 'חלונית מפרשים מימין (Ctrl+Shift+J)',
)

const sideLeftCommentaryTitle = computed(() =>
  props.sideLeftCommentaryVisible
    ? 'סגור חלונית מפרשים משמאל (Ctrl+Alt+J)'
    : 'חלונית מפרשים משמאל (Ctrl+Alt+J)',
)

// ── Overflow ───────────────────────────────────────────────────────────────────
//
// A pane too narrow (or, for a side toolbar, too short) for every button collapses its
// least essential controls into a "more" flyout, worst-first in the order
// TOOLBAR_OVERFLOW_ORDER fixes. The composable owns the fit arithmetic; what stays here is
// which of those controls this book actually has, how much room the pinned ones take, and
// the flyout's state.
const toolbarEl = ref<HTMLElement | null>(null)
const isVerticalToolbar = computed(
  () => toolbarPosition.value === 'left' || toolbarPosition.value === 'right',
)

const hasVersions = computed(() => props.versions.length > 0)

/** Whether the word-link markers control is on the toolbar - it tells us; see its own note. */
const wordLinkMarkersPresent = ref(false)

/** The collapsible controls this book renders, in collapse order. */
const presentOverflowKeys = computed<ToolbarOverflowKey[]>(() =>
  // Export to Word, the diacritics cycle and the zoom pair are on every book; only the
  // commentary sync depends on the book having commentaries at all.
  TOOLBAR_OVERFLOW_ORDER.filter((key) =>
    key === 'sync-commentaries' ? props.hasCommentaries : true,
  ),
)

/**
 * The room the pinned controls take, in button-widths - they always have theirs, so it comes
 * off before anything collapsible is measured. Counted the same way the template renders
 * them, conditions included.
 *
 * Every condition here must match the `v-if` it stands for, including the word-link markers
 * dropdown's, which lives in that component rather than in this template. An undercount is
 * not a rounding error: the toolbar believes it has a button's more room than it does and
 * collapses one control too few, so a button is left overflowing the pane.
 */
const pinnedButtonCount = computed(() => {
  const alwaysOn = 3 // TOC, previous section, next section
  const dropdowns = (props.hasRelatedBooks ? 1 : 0) + (hasVersions.value ? 1 : 0)
  const commentaryToggles = props.hasCommentaries
    ? 1 + (props.canUseSidePanel ? 2 : 0) // bottom panel, plus both side panels when they fit
    : 0
  const search = 1
  // Reported by the control itself rather than predicted here: it renders out of a list it
  // fetches, so nothing on this side can know in advance whether it is there.
  const wordLinkMarkers = wordLinkMarkersPresent.value ? 1 : 0
  // One separator, not two: the other one introduces the zoom pair and collapses with it,
  // so it is the zoom control's own cost rather than a pinned one.
  return (
    alwaysOn +
    dropdowns +
    commentaryToggles +
    search +
    wordLinkMarkers +
    TOOLBAR_SEPARATOR_IN_BUTTONS
  )
})

const { hasToolbarOverflow, overflowedKeys, toolbarButtonVisible } = useBookViewToolbarOverflow(
  toolbarEl,
  presentOverflowKeys,
  pinnedButtonCount,
  isVerticalToolbar,
)

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

defineExpose({ tocBtnRef })
</script>

<template>
  <div ref="toolbarEl" class="book-view-toolbar" :class="`toolbar-${toolbarPosition}`">
    <button
      ref="tocBtnRef"
      :class="{ active: tocVisible }"
      :disabled="!hasToc"
      title="תוכן עניינים (Ctrl+K)"
      @click="$emit('toggleToc')"
    >
      <IconTreeRtl />
    </button>
    <button
      :disabled="!hasToc"
      title="קטע הקודם (Ctrl+חץ ימני)"
      @click="$emit('navigateToPreviousSection')"
    >
      <IconChevronRight20Regular />
    </button>
    <button
      :disabled="!hasToc"
      title="קטע הבא (Ctrl+חץ שמאלי)"
      @click="$emit('navigateToNextSection')"
    >
      <IconChevronLeft20Regular />
    </button>
    <BookViewRelatedBooksDropdown
      v-if="hasRelatedBooks"
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
      :versions="versions"
      :active-version-id="activeVersionId"
      @select="$emit('selectVersion', $event)"
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
      :class="{ active: sideCommentaryVisible }"
      :title="sideCommentaryTitle"
      @click="$emit('toggleSideCommentary')"
    >
      <IconLayoutColumnTwoFocusRight20Filled v-if="sideCommentaryVisible" />
      <IconLayoutColumnTwo20Regular v-else />
    </button>
    <button
      v-if="hasCommentaries"
      :class="{ active: bottomCommentaryVisible }"
      :title="bottomCommentaryTitle"
      @click="$emit('toggleBottomCommentary')"
    >
      <IconLayoutRowTwoFocusBottom20Filled v-if="bottomCommentaryVisible" />
      <IconLayoutRowTwo20Regular v-else />
    </button>
    <button
      v-if="canUseSidePanel && hasCommentaries"
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
      class="collapsible"
      :class="{ active: autoSelectTopLine, collapsed: !toolbarButtonVisible('sync-commentaries') }"
      :inert="!toolbarButtonVisible('sync-commentaries')"
      :title="autoSelectTopLineTitle"
      @click="bookViewStore.toggleAutoSelectTopLine()"
    >
      <IconTimeline20Filled v-if="autoSelectTopLine" />
      <IconTimeline20Regular v-else />
    </button>
    <button
      :class="{ active: searchVisible }"
      title="חיפוש (Ctrl+F)"
      @click="$emit('toggleSearch')"
    >
      <IconSearch20Regular />
    </button>

    <!-- The separator belongs to the zoom pair it introduces, so it goes when they go. -->
    <div aria-hidden="true" class="separator collapsible" :class="{ collapsed: !toolbarButtonVisible('zoom') }" />

    <button
      class="collapsible"
      :class="{ collapsed: !toolbarButtonVisible('zoom') }"
      :title="zoomOutTitle"
      :inert="!toolbarButtonVisible('zoom')"
      :disabled="zoom <= ZOOM_CONFIG.MIN && commentaryZoom <= ZOOM_CONFIG.MIN"
      @pointerdown="startContinuousZoom('out')"
      @pointerup="stopContinuousZoom"
      @pointerleave="stopContinuousZoom"
      @pointercancel="stopContinuousZoom"
    >
      <IconZoomOut20Regular />
    </button>
    <button
      class="collapsible"
      :class="{ collapsed: !toolbarButtonVisible('zoom') }"
      :title="zoomInTitle"
      :inert="!toolbarButtonVisible('zoom')"
      :disabled="zoom >= ZOOM_CONFIG.MAX && commentaryZoom >= ZOOM_CONFIG.MAX"
      @pointerdown="startContinuousZoom('in')"
      @pointerup="stopContinuousZoom"
      @pointerleave="stopContinuousZoom"
      @pointercancel="stopContinuousZoom"
    >
      <IconZoomIn20Regular />
    </button>

    <div aria-hidden="true" class="separator" />

    <button
      class="collapsible"
      :inert="!toolbarButtonVisible('diacritics')"
      :class="[
        'diacritics-btn',
        { collapsed: !toolbarButtonVisible('diacritics') },
        { 'state-1': diacriticsState === 1, 'state-2': diacriticsState === 2 },
      ]"
      :title="diacriticsTitle"
      @click="onDiacriticsClick()"
    >
      <BookViewDiacriticsGlyph :state="diacriticsState" />
    </button>
    <BookViewWordLinkMarkersDropdown
      :book-id="bookId"
      @presence-change="wordLinkMarkersPresent = $event"
    />

    <button
      class="collapsible"
      :class="{ collapsed: !toolbarButtonVisible('export-to-word') }"
      :inert="!toolbarButtonVisible('export-to-word')"
      :title="EXPORT_TO_WORD_LABEL"
      @click="$emit('exportToWord')"
    >
      <BookViewExportToWordGlyph />
    </button>
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
        The one row that dismisses the flyout, because it is the one row that is finished
        when you pick it: the others are toggles and a zoom you watch, and closing the menu
        under someone about to press again is the wrong answer for all of them.
      -->
      <button
        v-if="isOverflowed('export-to-word')"
        role="menuitem"
        class="overflow-row"
        @click="overflowOpen = false; $emit('exportToWord')"
      >
        <BookViewExportToWordGlyph />
        <span>{{ EXPORT_TO_WORD_LABEL }}</span>
      </button>
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
      <button
        v-if="isOverflowed('diacritics')"
        role="menuitem"
        class="overflow-row"
        :class="[
          'diacritics-btn',
          { 'state-1': diacriticsState === 1, 'state-2': diacriticsState === 2 },
        ]"
        :title="diacriticsTitle"
        @click="onDiacriticsClick()"
      >
        <BookViewDiacriticsGlyph :state="diacriticsState" />
        <span>{{ diacriticsRowLabel }}</span>
      </button>
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
    </BookViewToolbarOverflowMenu>
  </div>
</template>

<style scoped>
.book-view-toolbar {
  /* How far the anchored more button sits from the edge it is pinned to, and therefore how
     much room the button row must leave for it: its own size plus that inset, twice over so
     the row stays centred on the toolbar rather than on the space left over. */
  --toolbar-edge-inset: 2px;
  --toolbar-reserved-edge: calc(var(--toolbar-button-size) + 2 * var(--toolbar-edge-inset));
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
  padding: var(--toolbar-horizontal-padding);
  background: var(--bg-toolbar);
  flex-shrink: 0;
  transition: background 120ms;
}

/* ── Orientation ── */
.toolbar-top,
.toolbar-bottom {
  flex-direction: row;
  height: var(--toolbar-horizontal-height);
  justify-content: center;
  /* Both sides, so the buttons stay centred on the toolbar itself and do not drift right as
     the reserved edge is taken out of one side only. */
  padding-inline: var(--toolbar-reserved-edge);
}
.toolbar-left,
.toolbar-right {
  flex-direction: column;
  justify-content: flex-start;
  width: var(--toolbar-vertical-width);
  height: auto;
  padding: var(--toolbar-vertical-padding);
  /* The column's own end, where the anchored button sits. Only that end needs reserving -
     a vertical toolbar starts its buttons at the top rather than centring them. */
  padding-block-end: var(--toolbar-reserved-edge);
}

/* ── Borders ── */
.toolbar-top {
  border-bottom: 1px solid var(--border-color);
}
.toolbar-bottom {
  border-top: 1px solid var(--border-color);
}
.toolbar-left {
  border-right: 1px solid var(--border-color);
}
.toolbar-right {
  border-left: 1px solid var(--border-color);
}

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

   So it stays in the flow and shrinks: width and padding to zero, the glyph fading as it
   goes. Its neighbours slide into the space it gives up rather than teleporting past it,
   and the same animation run backwards is the button reappearing when the room comes back.

   Width is animatable here precisely because these are fixed-size icon buttons - there is
   no intrinsic width to discover, so there is no `auto` to interpolate to and none of the
   usual reasons this does not work apply. */
.collapsible {
  overflow: hidden;
  /* Slow enough to read as one control leaving rather than a row rearranging itself, on an
     ease-in-out so it starts and ends at rest - the sharper decelerating curve used for
     presses made the neighbours look flung into place.

     The glyph fades FASTER than the width closes, and out of step with it on purpose: the
     button is already faint by the time the space it holds starts to go, so the eye follows
     one thing disappearing instead of watching a shrinking icon get squeezed. Reappearing
     runs the same way round - the space opens first and the glyph arrives into it. */
  transition:
    width 260ms cubic-bezier(0.4, 0, 0.2, 1),
    height 260ms cubic-bezier(0.4, 0, 0.2, 1),
    margin 260ms cubic-bezier(0.4, 0, 0.2, 1),
    padding 260ms cubic-bezier(0.4, 0, 0.2, 1),
    opacity 140ms ease;
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

/* The whole effect is motion for its own sake as far as a reader who has asked for less of
   it is concerned - the control still goes, it just goes at once. */
@media (prefers-reduced-motion: reduce) {
  .collapsible {
    transition: none;
  }
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
  left: var(--toolbar-edge-inset);
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
    color 120ms,
    opacity 260ms ease,
    visibility 260ms;
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


/* ── Separators ── */
.separator {
  background: var(--border-color);
  flex-shrink: 0;
}
.toolbar-top .separator,
.toolbar-bottom .separator {
  width: 1px;
  height: 18px;
  margin: 0 2px;
}
.toolbar-left .separator,
.toolbar-right .separator {
  width: 18px;
  height: 1px;
  margin: 2px 0;
}

/* ── Diacritics ── */
.diacritics-btn.state-1 {
  color: var(--diacritics-1);
}
.diacritics-btn.state-2 {
  color: var(--diacritics-2);
}
</style>
