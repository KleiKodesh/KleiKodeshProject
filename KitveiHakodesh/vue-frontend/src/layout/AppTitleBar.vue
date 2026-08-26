<script setup lang="ts">
import { ref, computed, defineAsyncComponent, watch, nextTick } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import { useAppShellPane } from '@/composables/useAppShellPane'
import {
  IconLineHorizontal320Regular,
  IconHome20Regular,
  IconOptions24Regular,
  IconOptions24Filled,
  IconConvertToText24Regular,
  IconSearch24Regular,
  IconChevronDoubleDown16Regular,
  IconSplitVertical20Regular,
  IconSplitVertical20Filled,
  // IconColor24Regular,
  // IconColor24Filled,
} from '@iconify-prerendered/vue-fluent'
import ThemeToggle from '@/theme/ThemeToggle.vue'
// The dropdown is v-if — lazy-load it so its imports (including fluent-color icons)
// don't add to the cold-start parse cost. It loads on first open, which is imperceptible.
const AppTitleBarNavDropdown = defineAsyncComponent(() => import('./AppTitleBarNavDropdown.vue'))
const AddressBar = defineAsyncComponent(() => import('./AddressBar.vue'))
import AppTitleBarTocBreadcrumb from './AppTitleBarTocBreadcrumb.vue'
import AppTitleBarHistoryButton from './AppTitleBarHistoryButton.vue'
import WorkspaceSubmenu from './WorkspaceSubmenu.vue'
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import { useAppTitleBarTocBreadcrumb } from './useAppTitleBarTocBreadcrumb'
import { useAppTitleBarShortcuts } from './useAppTitleBarShortcuts'
import { useSplitViewAvailable } from './useSplitViewAvailable'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { documentIcon } from '@/utils/documentIcons'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const tabStore = useTabStore()
const pdfOcrStore = usePdfOcrStore()

// ---- The workspace picker -----------------------------------------------------
//
// This is where workspaces lives now, and the only place: not on the nav rail, not in its
// overflow flyout, not in the hamburger menu. It is not a destination - there is no page
// and no route - so the button opens the picker in place (WorkspaceSubmenu) rather than a
// tab, which is why it sits beside home rather than among the menu's destinations.
//
// Its glyph is drawn in the bar's own colour, deliberately not the colourful one the icon
// table hands out: every other button in this bar is monochrome, and one coloured icon in
// the row read as a badge rather than as a control.
const workspacesIcon = documentIcon('apps')
const workspacesOpen = ref(false)
const workspacesButtonEl = ref<HTMLElement | null>(null)
const workspacesSubmenu = ref<InstanceType<typeof WorkspaceSubmenu> | null>(null)

const { titleBarVisible } = useUiChromeVisibility(props.paneId)

const isSplitViewAvailable = useSplitViewAvailable()

// ── TOC breadcrumb ────────────────────────────────────────────────────────────

const {
  segments: tocBreadcrumbSegments,
  rootTocEntries: tocBreadcrumbRootTocEntries,
  rootPdfEntries: tocBreadcrumbRootPdfEntries,
  plainSegmentLabels: tocBreadcrumbPlainLabels,
} = useAppTitleBarTocBreadcrumb(
  () => activeTab.value?.route,
  () => activeTab.value?.tocPath,
  () => pane.activeTabId.value,
  (tabId) => bookViewStore.getTocBridge(tabId),
  (tabId) => bookViewStore.getPdfBridge(tabId),
)

function onNavigateToBreadcrumbEntry(entry: import('@/webview-host/queries.types').TocEntry) {
  bookViewStore.getTocBridge(pane.activeTabId.value)?.navigateToEntry(entry)
}

function onNavigateToPdfBreadcrumbEntry(entry: import('@/stores/bookViewStore').PdfOutlineEntry) {
  bookViewStore.getPdfBridge(pane.activeTabId.value)?.navigateToEntry(entry)
}

// ── Button visibility helpers ─────────────────────────────────────────────────

function isTitleBarButtonVisible(buttonId: string): boolean {
  return !settingsStore.titleBarHiddenButtons.includes(buttonId)
}

const activeTab = computed(() => pane.activeTab.value)
const navDropdownOpen = ref(false)
const barRef = ref<HTMLElement | null>(null)
const navBtnRef = ref<HTMLElement | null>(null)

// const isPdfTab = computed(
//   () => activeTab.value?.route === '/pdf-view' || activeTab.value?.route === '/html-view',
// )

// bookViewStore.isBookViewActive and isTxtViewActive read from tabStore.activeTab (pane 1).
// For pane 2 we compute these directly from the pane's active tab.
const isBookViewActive = computed(() => activeTab.value?.route === '/book-view')
const isTxtViewActive = computed(() => activeTab.value?.route === '/txt-view')

// A click always enters search mode; the address-bar dropdown lists recent
// locations (shown while the field is empty / has no results) — not open tabs.
const barTitleHint = 'לחץ לניווט מהיר ולמקומות אחרונים (Ctrl+E)'

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  return full ? full + '\n' + barTitleHint : barTitleHint
})

const toolbarTitle = computed(() => {
  const baseTitle = isBookViewActive.value
    ? bookViewStore.getToolbarVisible(props.paneId) ? 'הסתר סרגל כלים' : 'הצג סרגל כלים'
    : activeTab.value?.pdfViewerTitleBarVisible !== false ? 'הסתר סרגל כותרת PDF' : 'הצג סרגל כותרת PDF'
  return `${baseTitle} (Ctrl+B)`
})

// const pdfFilterTitle = computed(() =>
//   settingsStore.pdfPageFilters ? 'בטל החלת ערכת נושא על דפי PDF' : 'החל ערכת נושא על דפי PDF',
// )

// ── Title-bar search (Explorer-style address bar) ─────────────────────────────
// The title becomes an editable search field, reusing the home-page search.
// A single click always enters search mode — the address bar's dropdown shows
// recent locations while the field is empty (or has no results). The open-tab
// list is a separate thing entirely, and native-only (see the layout README).
const searchMode = ref(false)

function enterSearchMode() {
  // The address bar lives inside the header — make sure it's visible first
  // (Ctrl+H may have hidden it).
  titleBarVisible.value = true
  searchMode.value = true
  // Opening the box IS the lesson, so this is where the hint retires - whether the
  // reader got here by clicking, by Ctrl+E, or from the tab strip.
  settingsStore.completeAddressBarHint()
}

function onTitleBarClick() {
  if (searchMode.value) return
  enterSearchMode()
}

// The nav sidebar is this menu, always on - so while it is up there is nothing left for
// the menu to offer. Both ways in are closed off: the button below is not rendered, and
// Ctrl+M does nothing.
function toggleNavDropdown() {
  if (settingsStore.getNavSidebarVisible(props.paneId)) return
  navDropdownOpen.value = !navDropdownOpen.value
}

/**
 * Ctrl+E. Also Ctrl+T where there is no native tab strip (VSTO task pane, dev browser):
 * the address bar's dropdown doubles as the tab list there (empty field = tab list).
 */
// ?? Expand-chevron squeeze rule ?????????????????????????????????????????????
// The chevron is a decoration; the breadcrumb is the content. When the bar runs
// out of room the breadcrumb starts clipping, and at that point the chevron's
// reserved strip is width the breadcrumb could have used ? so it goes,
// and .bar-title drops the padding that held its place (.is-cramped).
//
// Measured, not guessed at from a width breakpoint: how much room a breadcrumb
// needs depends on the book title and how deep the reader is in the TOC, so the
// same bar width is roomy for one tab and cramped for the next.
//
// The test asks the DESCENDANTS, not .bar-title itself and not just its direct
// children. Two layers make that necessary:
//   - .bar-title never overflows. Its labels shrink and clip inside their OWN
//     boxes (flex-shrink + overflow: hidden), so its scrollWidth stays equal to
//     its clientWidth no matter how badly the text is cut.
//   - .toc-breadcrumb is `display: contents`. That removes its BOX, not its DOM
//     node, so .bar-title has exactly one element child ? the wrapper ? and a
//     box-less element measures 0/0. Testing direct children alone therefore
//     compares 0 against 0 forever and never reports a squeeze, while the real
//     segments underneath it are clipped by hundreds of pixels.
// So walk the labels themselves, wherever they sit.
const barTitleRef = ref<HTMLElement | null>(null)
const isTitleCramped = ref(false)

// The clipping boxes: every element that carries overflow:hidden + flex-shrink
// in the resting bar. .bar-title-expand is absolutely positioned and is not one
// of them, so it cannot make the bar look cramped to itself.
const CLIPPABLE_LABELS =
  '.breadcrumb-title-name, .breadcrumb-segment, .bar-title-name, .bar-toc-segment, .bar-toc-path'

// 1px of slack: fractional layout at non-integral zoom/DPI leaves scrollWidth a
// hair above clientWidth on text that is not actually clipped, which would hide
// the chevron on a perfectly roomy bar.
const CLIP_SLACK = 1

function measureTitleCramped() {
  const el = barTitleRef.value
  if (!el) return
  const labels = el.querySelectorAll<HTMLElement>(CLIPPABLE_LABELS)
  isTitleCramped.value = Array.from(labels).some(
    (label) => label.scrollWidth - label.clientWidth > CLIP_SLACK,
  )
}

// The bar resizing is one trigger (window, split-view divider, sidebar toggle);
// the CONTENT changing is the other, and it fires no resize at all when the new
// title happens to fill the same box. Re-measure after the DOM settles, since
// the labels' widths are only known once Vue has patched them in.
//
// border-box, not the default content-box: toggling .is-cramped rewrites
// padding-left, which moves the CONTENT box while the border box holds
// still. Observing the content box would make this callback re-trigger itself on
// its own padding write ? a self-inflicted notification storm ("ResizeObserver
// loop completed with undelivered notifications") for a size change nothing
// outside this component made. The border box only moves when the BAR really
// resizes, which is the only thing worth re-measuring for.
useResizeObserver(barTitleRef, measureTitleCramped, { box: 'border-box' })

// ---- Discoverability hint -----------------------------------------------------
//
// The resting box looks like a label, so nothing about it says it opens. The chevron
// says so quietly; this makes it say so once, loudly, at the only moment the box has
// something worth opening for - the first time there is a recent location behind it.
//
// It runs until the reader opens the box, then stops for good
// (settingsStore.addressBarHintDone). Clicking is the whole lesson, so performing it
// IS the dismissal - there is nothing else to teach afterwards.
//
// Gated on the chevron actually being rendered: .is-cramped removes it, and animating
// an element that is not there would show nothing while the hint counted as spent.
const isExpandHintLive = computed(
  () =>
    !settingsStore.addressBarHintDone &&
    !isTitleCramped.value &&
    tabStore.recentLocations.length > 0,
)

// Watch the rendered LABELS, not the entry objects. The measurement depends on
// the text that reaches the DOM and nothing else, so the sources are the title
// and the label strings ? deep-traversing every segment's siblings/children
// arrays of full TocEntry objects would cost a large walk per change to learn
// something none of those fields affect. The root-entry COUNTS are in because
// they flip the template between the breadcrumb and the plain-title branch
// (v-if below), which swaps out the whole set of measured elements.
watch(
  () => [
    barTitle.value,
    tocBreadcrumbSegments.value.map((segment) => segment.label).join(' '),
    tocBreadcrumbPlainLabels.value.join(' '),
    tocBreadcrumbRootTocEntries.value.length,
    tocBreadcrumbRootPdfEntries.value.length,
  ],
  () => nextTick(measureTitleCramped),
  { immediate: true },
)

// Text metrics change when a webfont finishes loading, with no resize and no
// source change ? a title measured against the fallback face can clip (or stop
// clipping) once the real one lands. document.fonts.ready settles after the
// initial load; guarded because it is absent in some embedded WebView builds.
void document.fonts?.ready.then(() => measureTitleCramped())

function toggleAddressBar() {
  if (searchMode.value) searchMode.value = false
  else enterSearchMode()
}

useAppTitleBarShortcuts({
  paneId: props.paneId,
  pane,
  titleBarVisible,
  isSplitViewAvailable,
  toggleAddressBar,
  toggleNavDropdown,
})

</script>

<template>
  <!-- Keyboard event listener is always active (above), but only render the visual header when titleBarVisible is true -->
  <div ref="barRef" class="title-bar-container" :class="{ hidden: !titleBarVisible }">
    <header class="title-bar" @click="onTitleBarClick">
    <div class="bar-start">
      <div class="nav-btn-wrap">
        <button
          v-if="isTitleBarButtonVisible('hamburger') && !settingsStore.getNavSidebarVisible(props.paneId)"
          ref="navBtnRef"
          class="bar-btn"
          tabindex="-1"
          title="תפריט (Ctrl+M)"
          @click.stop="toggleNavDropdown"
        >
          <IconLineHorizontal320Regular />
        </button>
      </div>
      <button
        v-if="isTitleBarButtonVisible('split-view') && isSplitViewAvailable && !settingsStore.getNavSidebarVisible(props.paneId)"
        class="bar-btn"
        tabindex="-1"
        :title="bookViewStore.splitViewEnabled ? 'סגור תצוגה מפוצלת (Ctrl+|)' : 'פתח תצוגה מפוצלת (Ctrl+|)'"
        @click.stop="bookViewStore.toggleSplitView()"
      >
        <IconSplitVertical20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconSplitVertical20Regular v-else />
      </button>
      <ThemeToggle v-if="isTitleBarButtonVisible('theme-toggle')" tabindex="-1" />
      <button
        v-if="isTxtViewActive"
        class="bar-btn"
        tabindex="-1"
        title="חיפוש בטקסט (Ctrl+F)"
        @click.stop="bookViewStore.txtViewToggleSearch(props.paneId)"
      >
        <IconSearch24Regular />
      </button>
      <!-- <button
        v-if="isTitleBarButtonVisible('pdf-filter') && isPdfTab"
        class="bar-btn"
        tabindex="-1"
        :title="pdfFilterTitle"
        @click.stop="settingsStore.togglePdfPageFilters()"
      >
        <IconColor24Filled v-if="settingsStore.pdfPageFilters" />
        <IconColor24Regular v-else />
      </button> -->
      <button
        v-if="isTitleBarButtonVisible('toolbar-toggle') && (isBookViewActive || activeTab?.route === '/pdf-view')"
        class="bar-btn"
        tabindex="-1"
        :title="toolbarTitle"
        @click.stop="isBookViewActive ? bookViewStore.toggleToolbar(props.paneId) : pane.togglePdfViewerTitleBar()"
      >
        <IconOptions24Filled v-if="isBookViewActive ? bookViewStore.getToolbarVisible(props.paneId) : activeTab?.pdfViewerTitleBarVisible !== false" />
        <IconOptions24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('ocr') && activeTab?.route === '/pdf-view'"
        class="bar-btn"
        tabindex="-1"
        :class="{ active: pdfOcrStore.isActive }"
        title="בחירת טקסט באזור (OCR)"
        @click.stop="pdfOcrStore.toggle()"
      >
        <IconConvertToText24Regular />
      </button>
    </div>

    <!-- Search mode — the title turns into an editable address-bar search. -->
    <AddressBar
      v-if="searchMode"
      :pane-id="props.paneId"
      class="bar-search"
      @close="searchMode = false"
    />

    <span
      v-else
      ref="barTitleRef"
      class="bar-title"
      :class="{ 'is-cramped': isTitleCramped }"
      dir="rtl"
      :title="barTitle"
    >
      <!-- Interactive breadcrumb for book-view and pdf-view tabs -->
      <AppTitleBarTocBreadcrumb
        v-if="tocBreadcrumbSegments.length > 0 || tocBreadcrumbRootTocEntries.length > 0 || tocBreadcrumbRootPdfEntries.length > 0"
        :book-title="activeTab?.title ?? ''"
        :segments="tocBreadcrumbSegments"
        :root-toc-entries="tocBreadcrumbRootTocEntries"
        :root-pdf-entries="tocBreadcrumbRootPdfEntries"
        @navigate-to-toc-entry="onNavigateToBreadcrumbEntry"
        @navigate-to-pdf-entry="onNavigateToPdfBreadcrumbEntry"
      />
      <!-- Plain title, plus the tab's breadcrumb caption while a TOC-bearing tab's
           bridge has not registered yet. plainSegmentLabels is route-gated by the
           composable, so a non-TOC route never renders one. -->
      <template v-else>
        <span class="bar-title-name">{{ activeTab?.title }}</span>
        <template v-for="segment in tocBreadcrumbPlainLabels" :key="segment">
          <AppTitleBarBreadcrumbChevronDropdown :siblings="[]" :active-sibling-id="null" />
          <span class="bar-toc-segment"><bdi>{{ segment }}</bdi></span>
        </template>
      </template>
      <!-- Expand affordance: the resting box is a collapsed address bar, and this
           says so. Gone in search mode ? .bar-search replaces this whole span, so
           the mark disappears exactly when the field is expanded. -->
      <IconChevronDoubleDown16Regular v-if="!isTitleCramped" class="bar-title-expand"
        :class="{ 'is-hinting': isExpandHintLive }" />
    </span>

    <div class="bar-end">
      <!-- Stays put whether or not this pane's rail is up, unlike the hamburger and the
           split-view toggle: those two are about the rail and the window, so the rail can
           speak for them. Home is a destination, and the bar is where it belongs. -->
      <button v-if="isTitleBarButtonVisible('home')" class="bar-btn" tabindex="-1" title="בית (Ctrl+G)" @click.stop="pane.goHome()"><IconHome20Regular /></button>
      <button
        v-if="isTitleBarButtonVisible('workspaces')"
        ref="workspacesButtonEl"
        class="bar-btn"
        :class="{ active: workspacesOpen }"
        tabindex="-1"
        title="סביבות עבודה"
        :aria-expanded="workspacesOpen"
        @click.stop="workspacesSubmenu?.toggle()"
      >
        <component :is="workspacesIcon.icon24" />
      </button>
      <!-- Back / Forward through the ACTIVE TAB's own history, like a browser —
           not between tabs (Ctrl+Tab still does that). Click steps once
           (Alt+ArrowRight / Alt+ArrowLeft), press-and-hold opens the full list.
           The 'prev-tab'/'next-tab' visibility ids are legacy — they key the
           persisted hidden-buttons setting, so renaming them is a migration. -->
      <AppTitleBarHistoryButton
        v-if="isTitleBarButtonVisible('prev-tab')"
        :pane-id="props.paneId"
        direction="back"
      />
      <AppTitleBarHistoryButton
        v-if="isTitleBarButtonVisible('next-tab')"
        :pane-id="props.paneId"
        direction="forward"
      />
    </div>

  </header>

  <!-- Nav dropdown — kept outside header so it stays visible when header is hidden -->
  <AppTitleBarNavDropdown
    v-if="navDropdownOpen"
    :toggle-button-el="navBtnRef"
    @close="navDropdownOpen = false"
    @click.stop
  />

  <!-- Outside the header for the same reason as the dropdown: it must survive the header
       being hidden. It hangs straight down from the button, right edges aligned. -->
  <WorkspaceSubmenu
    ref="workspacesSubmenu"
    v-model:open="workspacesOpen"
    :anchor="workspacesButtonEl"
  />
  </div>
</template>

<style scoped>
/* ── Title bar layout — Explorer address-bar model ────────────────────────────
 * Three-zone layout: left buttons | address-bar box | right buttons.
 *   - .bar-start / .bar-end are flex: 0 0 auto → each hugs its buttons.
 *   - .bar-title (and .bar-search in search mode) is flex: 1 1 auto → the
 *     bordered box FILLS all remaining width between the two button groups,
 *     like the Windows Explorer address bar. Not centered, not content-sized.
 *   - min-width: 0 + overflow: hidden on .bar-title lets it shrink below its
 *     natural content width so the inner text ellipsizes.
 *
 * The persistent border on .bar-title is intentional — it's the affordance that
 * signals the bar is a clickable input. Search mode swaps .bar-title for the
 * editable .bar-search (AddressBar) in the same footprint.
 * ──────────────────────────────────────────────────────────────────────────── */
.title-bar-container {
  position: relative;
}
.title-bar-container.hidden .title-bar {
  display: none;
}
.title-bar {
  display: flex;
  align-items: center;
  height: var(--title-bar-height);
  padding: var(--title-bar-padding);
  background: var(--bg-secondary);
  /* No divider under the bar - the chrome runs continuously from the bar into the page. */
  position: relative;
  /* Regular arrow over the bar and the breadcrumb/title; only the buttons and
     breadcrumb chevrons use the pointer (hand). */
  cursor: default;
}
.bar-start {
  display: flex;
  align-items: center;
  gap: 0;
  flex: 0 0 auto;
}
.nav-btn-wrap {
  position: relative;
}
.bar-end {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0;
  flex: 0 0 auto;
}
/* The title/breadcrumb is styled as a Windows-Explorer address bar: a bordered
   box that FILLS the space between the button groups (not a centered, content-
   sized label). The persistent border is the affordance that tells the user the
   bar is a clickable input. Clicking it enters search mode (AddressBar), which
   reuses the very same box footprint (.bar-search) for a seamless swap. */
.bar-title {
  display: flex;
  align-items: center;
  /* Box fills the bar (Explorer address-bar), but the breadcrumb/title inside it
     is centered while inactive. Editing swaps in .bar-search, whose field is
     start-aligned for typing. */
  justify-content: center;
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  height: 24px;
  margin-inline: 6px;
  /* 6px of inset all round, plus room for the absolutely-positioned
     .bar-title-expand so a long title clips before it slides underneath.
     Reserved on BOTH sides even though the chevron only occupies one: this box
     CENTERS its content, so a one-sided reservation moves the content box off
     the border box's axis and the title sits visibly off-centre. Symmetric
     padding keeps the two concentric, which is what centering assumes, and it
     costs nothing in practice - the squeeze rule below hands the whole strip
     back to the breadcrumb the moment it has anything to clip. */
  padding-inline: 20px;
  position: relative;
  font-weight: 400;
  font-size: 0.82rem;
  color: var(--text-secondary);
  white-space: nowrap;
  cursor: default;
  /* Blend into the title bar (--bg-secondary) rather than stand out as a filled
     field — a subtle, uniform 1px frame is enough of an input hint. All four
     sides match at rest; the accent underline appears only on focus. */
  background: color-mix(in srgb, var(--text-primary) 3%, transparent);
  border: 1px solid var(--border-color);
  border-radius: 6px;
}
.bar-title:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
/* Pinned to the box's own edge rather than left in the flow, because .bar-title
   CENTERS its content ? in the flow the chevron would drift with the title's
   width and sit wherever the text happened to end. Absolute keeps it on the box's
   own edge, and out of the flow it cannot squeeze the title's available width.
   Deliberately faint: it marks the box as expandable, it is not a second control
   competing with the title, and .bar-title already carries the click. */
.bar-title-expand {
  position: absolute;
  /* Physical left, NOT inset-inline-start. Two reasons, and the second is the
     one that bites if this is ever "simplified" to a logical property:
     - The chevron is chrome on the box, not part of the RTL text flow, so it
       belongs to a physical edge rather than to wherever the text starts.
     - It has to stay on the same side as the trailing search button of the
       editable AddressBar that replaces this box in search mode, or the
       affordance jumps across the bar on a swap meant to be seamless. That
       button sits on the physical LEFT: .address-bar sets no direction, so it
       inherits RTL from the shell and its last flex child lands left.
     This box is dir="rtl", so inset-inline-start would resolve to the RIGHT
     and break the second point. */
  left: 4px;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
  opacity: 0.45;
  pointer-events: none;
}
.bar-title:hover .bar-title-expand {
  opacity: 0.75;
}

/* ---- The one-time discoverability hint --------------------------------------
   The chevron is deliberately faint at rest, which is right once you know what the
   box is and useless before then. While the hint is live it travels: it slides down
   into its slot, overshoots, rebounds, and settles - the movement a thing makes when
   it drops into place, which says "this opens" far better than brightening does.

   The chevron points DOWN, so the track is vertical and the slide runs the way the
   mark itself points - the motion and the symbol say the same thing.

   THREE RUNS, NOT INFINITE. Total motion is 3 x 900ms = 2.7s, deliberately under
   the five seconds at which WCAG 2.2 SC 2.2.2 (Pause, Stop, Hide, level A) starts
   requiring a pause control for motion that auto-starts alongside other content.
   That budget is CUMULATIVE, not per-cycle, so looping forever with a long quiet
   gap between runs would not have helped - it would have failed the criterion and
   been more intrusive besides. A hint cannot claim the "essential" exemption
   either: what it conveys is available statically, from the chevron just sitting
   there.

   Losing the loop costs nothing real. The hint is armed by a persisted flag, so a
   reader who misses all three runs is shown them again next launch, and every
   launch after, until they open the box once. It stops because it worked, not
   because a timer ran out mid-lesson.

   WHY linear() AND NOT cubic-bezier. The obvious choice is easeOutBack
   (cubic-bezier(0.34, 1.56, 0.64, 1)), whose y passes 1 so the mark travels beyond
   its resting place and comes back. At this size it does nothing: a bezier of that
   shape peaks about 10% past the target, and 10% of the 6px this mark has to move
   is half a pixel. Back-out curves need roughly 20-30px of travel to read as a
   bounce at all, and a 12px icon centred in a 24px box can never have that - the
   clearance to .bar-title's overflow: hidden is 6px, full stop.

   linear() is not held to a single ~10% overshoot. It samples a spring as explicit
   stops, so the peak is whatever the spring says: here about 1.45, which on 6px of
   travel is a 2.7px overshoot DOWNWARD - past the resting point toward the bottom
   edge, not further up - which is visible on a 12px mark and still inside the 6px
   clearance below it. The -6px start sits flush against the top clearance with
   nothing to spare, so this travel cannot be increased without clipping the first
   frame. It also carries the small SECOND rebound below, which a cubic-bezier
   cannot express at any amplitude: one overshoot is that curve's mathematical
   ceiling. Chrome/Edge 113+, Firefox 112+, Safari 17.2+, and this app ships on
   WebView2, so the @supports fallback below is a formality rather than a real
   branch - but a formality worth keeping, since it degrades to a real curve. */
/* Two animations, not one: the transform and the opacity need DIFFERENT timing
   functions, and a shared keyframe list cannot give them that. A single set of
   stops would also mean the opacity stop at the fade-in splits the travel in two,
   restarting the spring halfway and playing a truncated bounce twice. Split, each
   property gets its own curve over its own stops. */
@keyframes bar-title-expand-hint-move {
  /* Above the slot; the travel is done by the halfway mark. The rest of the cycle
     holds at the resting position, and that hold is what separates the three runs:
     back-to-back springs would blur into one long wobble instead of reading as
     three deliberate nudges. Spacing them this way keeps each bounce quick, where
     simply lengthening the cycle would have stretched the spring into slow motion. */
  from    { transform: translateY(-6px); }
  50%, to  { transform: translateY(0); }
}
@keyframes bar-title-expand-hint-fade {
  /* Fades in over the first sixth so a run begins as an arrival, not a blink, then
     sits at the resting opacity for the hold. The shorthand adds `backwards` so the
     element takes this 0 BEFORE the first frame - without it the base 0.45 paints
     for a frame first and the run opens on the blink this fade exists to avoid. */
  from { opacity: 0; }
  18%  { opacity: 1; }
  50%, to { opacity: 0.45; }
}
.bar-title-expand.is-hinting {
  /* Fallback for anything without linear(): a real back-out curve, which at this
     size lands as a plain slide with no perceptible bounce. Correct, just quieter. */
  animation:
    bar-title-expand-hint-move 900ms cubic-bezier(0.34, 1.56, 0.64, 1) 3,
    bar-title-expand-hint-fade 900ms ease-out 3 backwards;
}
@supports (animation-timing-function: linear(0, 1)) {
  .bar-title-expand.is-hinting {
    /* A spring sampled as stops: overshoots to ~1.45, rebounds to ~0.91, settles.
       Generated from spring parameters rather than hand-tuned - do not nudge these
       individually, regenerate the set if the motion needs to change.

       TWO values, comma-separated, because the shorthand above declares two
       animations and this longhand is matched against them in order. A single value
       would apply the spring to the opacity fade as well, overshooting it past full
       and clipping - which is exactly the spatial-vs-effects distinction that says
       position may bounce and opacity may not. The fade keeps its ease-out. */
    animation-timing-function:
      linear(
        0, 0.0632, 0.2278, 0.4471, 0.6784, 0.8944, 1.0759, 1.2124,
        1.3018, 1.3479, 1.4165, 1.4499, 1.4486, 1.4165, 1.36, 1.2866,
        1.2043, 1.1201, 1.0399, 0.9679, 0.9337, 0.9186, 0.9145, 0.9204,
        0.9344, 0.9544, 0.9781, 1.0032, 1.0142, 1.0192, 1.0182, 1.0122,
        1.0031, 0.9976, 0.9952, 0.9955, 0.9978, 1.0009, 1.0018, 1
      ),
      ease-out;
  }
}
/* Hover already brightens the chevron, and a mark sliding around under the cursor
   reads as a glitch. The reader is on the box at that point - the hint has served its
   purpose, so it stops and hands the element back to the plain hover state. */
.bar-title:hover .bar-title-expand.is-hinting {
  animation: none;
}
/* Declared at top level, NOT inside the @media below. Vue's scoped-style compiler
   rewrites @keyframes names to add the scope id and rewrites the animation-name
   references to match; doing that reliably for a keyframes rule nested inside an
   at-rule is not something to bet on, and if the name and the reference disagree the
   animation resolves to nothing. The failure would be silent and would land on
   exactly the readers this rule exists to serve. An unused keyframes rule is inert,
   so hoisting it costs nothing. */
@keyframes bar-title-expand-hint-reduced {
  from, to { opacity: 0.45; }
  50%      { opacity: 1; }
}
@media (prefers-reduced-motion: reduce) {
  .bar-title-expand.is-hinting {
    /* A REPLACEMENT, not a removal. Dropping to `animation: none` would leave the
       one group that cannot fall back on having noticed the movement with no hint at
       all. So the mark still calls attention to itself - it just brightens in place.

       Opacity only, no transform: WCAG 2.3.3 excludes colour and opacity changes
       from what counts as motion animation, so this is a genuine substitute rather
       than a smaller dose of the same thing. Same three runs, same duration.

       One animation where the rule above declares two: `animation` is a shorthand,
       so naming a single one resets the whole list and cancels the move. */
    animation: bar-title-expand-hint-reduced 900ms ease-in-out 3;
  }
}
/* No chevron ? no reserved strip. Handing that 14px back is the point of hiding
   it: the breadcrumb gets the room instead of a blank gap where the mark was. */
.bar-title.is-cramped {
  padding-inline: 6px;
}
/* Search mode swaps in the editable AddressBar, occupying the same box. */
.bar-search {
  flex: 1 1 auto;
  min-width: 0;
  margin-inline: 6px;
}
/* Block pointer events on text spans so clicks bubble to the header toggle,
   but leave buttons (chevrons) fully interactive. */
.bar-title .breadcrumb-title-name,
.bar-title .bar-title-name,
.bar-title .bar-toc-segment,
.bar-title .bar-toc-path,
.bar-title .breadcrumb-segment {
  pointer-events: none;
}
.bar-title-name {
  unicode-bidi: isolate;
  direction: ltr;
}
.bar-toc-path {
  color: var(--text-secondary);
  opacity: 0.7;
}
.bar-toc-segment {
  color: var(--text-secondary);
  opacity: 0.7;
  overflow: hidden;
  text-overflow: clip;
  white-space: nowrap;
  flex-shrink: 1;
  min-width: 0;
  margin-inline-end: 2px;
  /* Cut off the START of the segment, keeping the tail visible: LTR paragraph
     clips at the right edge (the RTL text's start); the inner <bdi> keeps
     natural Hebrew rendering. Same trick as .breadcrumb-segment — and, like
     there, intentionally the opposite of the title, which clips its END
     (see the truncation note in AppTitleBarTocBreadcrumb.vue). */
  direction: ltr;
}
/* .bar-btn (and its .active state) is global now — see main.css. */

</style>
