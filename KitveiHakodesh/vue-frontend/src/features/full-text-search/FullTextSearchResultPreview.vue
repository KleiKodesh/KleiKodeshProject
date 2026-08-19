<script setup lang="ts">
/**
 * Fixed-height windowed live preview of a search result — replaces the clamped
 * snippet while open. Scrolling toward either edge loads more lines in that
 * direction (VueUse useInfiniteScroll); the composable trims the opposite edge
 * past its cap, so only a bounded window is ever in memory.
 */
import { ref, watch, nextTick } from 'vue'
import { useInfiniteScroll, until } from '@vueuse/core'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBooksDataStore } from '@/stores/booksDataStore'
import WordLinkTooltip from '@/features/book-view/lines/WordLinkTooltip.vue'
import { useWordLinkTooltip } from '@/features/book-view/lines/useWordLinkTooltip'
import BookViewAbbrevTooltip from '@/features/book-view/lines/BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from '@/features/book-view/lines/useBookViewAbbrevTooltip'
import type { PreviewState } from './useFullTextSearchPreview'

const props = defineProps<{
  state: PreviewState
  /** Censor pass — same one the snippet path uses. */
  renderHtml: (html: string) => string
  loadAbove: () => Promise<number>
  loadBelow: () => Promise<number>
  /** Reload the seed window around the matched line (it may have been trimmed out). */
  reseed: () => Promise<void>
}>()

const boxEl = ref<HTMLElement | null>(null)

// Blocks the mount-time edge checks until the initial scroll position is applied —
// otherwise the top loader fires at scrollTop 0 before the seed/restore lands.
const ready = ref(false)

useInfiniteScroll(boxEl, () => loadAnchored('bottom'), {
  distance: 30,
  direction: 'bottom',
  canLoadMore: () => ready.value && !props.state.atEnd && !props.state.loading,
})

useInfiniteScroll(boxEl, () => loadAnchored('top'), {
  distance: 30,
  direction: 'top',
  canLoadMore: () => ready.value && !props.state.atStart && !props.state.loading,
})

// A load can move content on BOTH sides of the viewport: prepends grow the height
// above it, and past the window cap the opposite edge is trimmed as well. Anchor on
// the line nearest the loading edge — it survives both the load and the far-edge
// trim — and shift scrollTop by however far it moved, keeping the visible text still
// (manual anchoring; overflow-anchor is disabled on the box so the browser doesn't
// also try to compensate).
async function loadAnchored(edge: 'top' | 'bottom') {
  const box = boxEl.value
  if (!box) return
  const rows = box.querySelectorAll<HTMLElement>('.preview-line')
  const anchor = edge === 'top' ? rows[0] : rows[rows.length - 1]
  const prevOffset = anchor?.offsetTop ?? 0
  if (!(await (edge === 'top' ? props.loadAbove() : props.loadBelow()))) return
  await nextTick()
  if (anchor?.isConnected) box.scrollTop += anchor.offsetTop - prevOffset
}

// Position once lines exist: restore the saved scroll (reopen, or remount after
// virtual-list recycling), else seed with the matched line at the window top.
let positioned = false
watch(
  () => props.state.lines.length,
  async (len) => {
    if (positioned || !len) return
    positioned = true
    await nextTick()
    const box = boxEl.value
    if (!box) return
    if (props.state.scrollTop > 0) {
      box.scrollTop = props.state.scrollTop
    } else {
      const matched = box.querySelector<HTMLElement>('[data-matched]')
      if (matched) box.scrollTop = matched.offsetTop
    }
    ready.value = true
    // Nudge the edge checks awake — arrivedState only refreshes on scroll events,
    // and setting scrollTop to an unchanged value (e.g. 0) emits none.
    box.dispatchEvent(new Event('scroll'))
  },
  { immediate: true },
)

function onScroll() {
  if (ready.value && boxEl.value) props.state.scrollTop = boxEl.value.scrollTop
}

/** Scroll the matched result line back to the top of the window (header recenter button).
 *  If the capped window has trimmed the matched line out, reload the seed around it first.
 *  2px headroom — smooth scrolling settles a hair past the target on fractional DPRs. */
async function recenter() {
  const box = boxEl.value
  if (!box) return
  let matched = box.querySelector<HTMLElement>('[data-matched]')
  if (!matched) {
    // Reseed no-ops while an edge load is in flight — wait it out so the click isn't
    // silently dropped.
    await until(() => props.state.loading).toBe(false)
    await props.reseed()
    await nextTick()
    matched = box.querySelector<HTMLElement>('[data-matched]')
    if (!matched) return
    // The window was replaced wholesale — jump, don't glide from an unrelated spot.
    box.scrollTop = Math.max(0, matched.offsetTop - 2)
    // Refresh saved scrollTop + edge state even if the assignment landed on the
    // current value (no native scroll event fires then).
    box.dispatchEvent(new Event('scroll'))
    return
  }
  box.scrollTo({ top: Math.max(0, matched.offsetTop - 2), behavior: 'smooth' })
}

defineExpose({ recenter })

// ── Word-level links (hover preview + click-through) ─────────────────────────

const paneNavigation = usePaneNavigation()
const booksDataStore = useBooksDataStore()
const {
  wordLinkTooltip,
  keepOpen: keepWordLinkTooltipOpen,
  releaseOpen: releaseWordLinkTooltip,
  beginSelection: beginWordLinkTooltipSelection,
} = useWordLinkTooltip(boxEl, {
  getBookTitle: (targetBookId) => booksDataStore.allBooksMap.get(targetBookId)?.title ?? '',
  onNavigate: (target) => {
    paneNavigation.openBookTarget({
      title: booksDataStore.allBooksMap.get(target.bookId)?.title ?? '',
      route: '/book-view',
      bookId: target.bookId,
      openTocLineIndex: target.lineIndex,
    })
  },
})

const { abbrevTooltip } = useBookViewAbbrevTooltip(boxEl)
</script>

<template>
  <div ref="boxEl" class="preview-box" @scroll.passive="onScroll">
    <WordLinkTooltip
      v-if="wordLinkTooltip"
      :key="wordLinkTooltip.id"
      :data="wordLinkTooltip"
      @pointer-enter="keepWordLinkTooltipOpen"
      @pointer-leave="releaseWordLinkTooltip"
      @select-start="beginWordLinkTooltipSelection"
    />
    <BookViewAbbrevTooltip
      v-if="abbrevTooltip"
      :key="abbrevTooltip.id"
      :data="abbrevTooltip"
    />
    <div v-if="state.loading && !state.lines.length" class="preview-empty">טוען…</div>
    <!-- eslint-disable-next-line vue/no-v-html -->
    <div
      v-for="line in state.lines"
      :key="line.id"
      class="preview-line"
      :class="{ matched: line.lineIndex === state.lineIndex }"
      :data-matched="line.lineIndex === state.lineIndex ? '' : undefined"
      v-html="renderHtml(line.html)"
    />
  </div>
</template>

<style scoped>
.preview-box {
  position: relative; /* line offsetTop must be box-relative for the seed scroll */
  height: calc(var(--line-height, 1.5) * 4.5em);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  overscroll-behavior: contain;
  overflow-anchor: none;
  direction: rtl;
  padding: 2px 0;
  font-family: var(--text-font);
  font-size: 1em;
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  text-align: justify;
  user-select: text;
}
.preview-line.matched {
  background: color-mix(in srgb, var(--accent-color) 7%, transparent);
  border-radius: 4px;
}
.preview-line :deep(mark) {
  background: transparent;
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
.preview-line :deep(.word-link) {
  color: var(--accent-color);
  cursor: pointer;
  text-decoration: underline dotted transparent;
  text-underline-offset: 3px;
  transition: text-decoration-color 100ms;
}
.preview-line :deep(.word-link:hover) {
  text-decoration-color: currentColor;
}
.preview-line :deep(.word-link-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
  color: var(--accent-color);
  cursor: pointer;
}
/* Label via CSS content — the marker must contribute zero text characters. */
.preview-line :deep(.word-link-marker)::before {
  content: attr(data-wl-label);
}
.preview-empty {
  color: var(--text-secondary);
  opacity: 0.7;
  font-size: 0.9em;
}
</style>
