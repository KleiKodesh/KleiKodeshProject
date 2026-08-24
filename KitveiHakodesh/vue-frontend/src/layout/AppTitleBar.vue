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
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import { useAppTitleBarTocBreadcrumb } from './useAppTitleBarTocBreadcrumb'
import { useAppTitleBarShortcuts } from './useAppTitleBarShortcuts'
import { useSplitViewAvailable } from './useSplitViewAvailable'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const pdfOcrStore = usePdfOcrStore()
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
// reserved trailing strip is width the breadcrumb could have used ? so it goes,
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
// padding-inline-end, which moves the CONTENT box while the border box holds
// still. Observing the content box would make this callback re-trigger itself on
// its own padding write ? a self-inflicted notification storm ("ResizeObserver
// loop completed with undelivered notifications") for a size change nothing
// outside this component made. The border box only moves when the BAR really
// resizes, which is the only thing worth re-measuring for.
useResizeObserver(barTitleRef, measureTitleCramped, { box: 'border-box' })

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
      <IconChevronDoubleDown16Regular v-if="!isTitleCramped" class="bar-title-expand" />
    </span>

    <div class="bar-end">
      <!-- Dropped while this pane's rail is up, the way the hamburger and the split-view
           toggle are: the rail carries home itself, and a control is never offered twice. -->
      <button v-if="isTitleBarButtonVisible('home') && !settingsStore.getNavSidebarVisible(props.paneId)" class="bar-btn" tabindex="-1" title="בית (Ctrl+G)" @click.stop="pane.goHome()"><IconHome20Regular /></button>
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
  /* Extra room on the PHYSICAL right for the absolutely-positioned
     .bar-title-expand, so a long centered title clips before it reaches the
     chevron instead of sliding underneath it. Physical (padding-right) to match
     the chevron's own physical anchoring ? see .bar-title-expand. */
  padding-inline: 6px;
  padding-right: 20px;
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
/* Pinned to the trailing edge rather than left in the flow, because .bar-title
   CENTERS its content ? in the flow the chevron would drift with the title's
   width and sit wherever the text happened to end. Absolute keeps it on the box's
   own edge, and out of the flow it cannot squeeze the title's available width.
   Deliberately faint: it marks the box as expandable, it is not a second control
   competing with the title, and .bar-title already carries the click. */
.bar-title-expand {
  position: absolute;
  /* Physical right, NOT inset-inline-end. This box is dir="rtl", so the logical
     end edge is the LEFT one ? but the editable AddressBar that replaces this
     box in search mode is not RTL, and puts its trailing search button on the
     physical right. Anchoring logically would jump the affordance across the bar
     on a swap that is supposed to be seamless. The chevron is chrome on the box,
     not part of the RTL text flow, so it follows the box. */
  right: 4px;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
  opacity: 0.45;
  pointer-events: none;
}
.bar-title:hover .bar-title-expand {
  opacity: 0.75;
}
/* No chevron ? no reserved strip. Handing the 20px back is the point of hiding
   it: the breadcrumb gets the room instead of a blank gap where the mark was. */
.bar-title.is-cramped {
  padding-right: 6px;
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
