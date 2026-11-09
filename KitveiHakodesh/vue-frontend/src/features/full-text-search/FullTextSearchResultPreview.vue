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
import { useLineNotesTooltip } from '@/features/book-view/lines/useLineNotes'
import BookViewAbbrevTooltip from '@/features/book-view/lines/BookViewAbbrevTooltip.vue'
import { useBookViewAbbrevTooltip } from '@/features/book-view/lines/useBookViewAbbrevTooltip'
import type { PreviewState } from './useFullTextSearchPreview'
import { teamimFontStyle } from '@/utils/teamimFont'

const props = defineProps<{
  state: PreviewState
  /** Censor pass — same one the snippet path uses. */
  renderHtml: (html: string) => string
  loadAbove: () => Promise<number>
  loadBelow: () => Promise<number>
  /** Reload the seed window around the matched line (it may have been trimmed out). */
  reseed: () => Promise<void>
  /** The result card's snippet HTML — the preview centres on this exact text. */
  snippetHtml: string
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
      if (matched) box.scrollTop = hitTop(box, matched)
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

// ── Locating the result card's snippet inside the matched line ─────────────────
// Compared on a folded stream: niqqud/teamim dropped, whitespace collapsed, "…" dropped
// (the card's cut marks). Each folded char keeps its DOM text node + offset, so the
// found span becomes a Range we can measure.

const isFoldedOut = (c: number) => (c >= 0x0591 && c <= 0x05c7 && c !== 0x05be) || c === 0x2026

function foldText(text: string): string {
  let out = ''
  for (const ch of text) {
    const c = ch.charCodeAt(0)
    if (isFoldedOut(c)) continue
    if (/\s/.test(ch)) { if (out && !out.endsWith(' ')) out += ' '; continue }
    out += ch
  }
  return out.trim()
}

/** The card snippet's [start, end) inside the matched line as a measurable Range, or null. */
function snippetRange(matched: HTMLElement): Range | null {
  const holder = document.createElement('div')
  holder.innerHTML = props.snippetHtml
  const words = foldText(holder.textContent ?? '').split(' ').filter(Boolean)
  if (!words.length) return null

  const chars: string[] = []
  const at: Array<[Text, number]> = []
  const walker = document.createTreeWalker(matched, NodeFilter.SHOW_TEXT)
  for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
    const t = node.data
    for (let i = 0; i < t.length; i++) {
      const c = t.charCodeAt(i)
      if (isFoldedOut(c)) continue
      if (/\s/.test(t[i]!)) {
        if (chars.length && chars[chars.length - 1] !== ' ') { chars.push(' '); at.push([node, i]) }
        continue
      }
      chars.push(t[i]!); at.push([node, i])
    }
  }
  const line = chars.join('')

  // The card may add a few context words from neighbouring lines, so try the full
  // snippet first, then shave words off its ends until both ends are found in order.
  for (let cut = 0; cut < Math.min(words.length, 8); cut++) {
    for (let lead = 0; lead <= cut; lead++) {
      const core = words.slice(lead, words.length - (cut - lead))
      if (!core.length) continue
      const head = core.slice(0, 3).join(' ')
      const tail = core.slice(-3).join(' ')
      const start = line.indexOf(head)
      if (start < 0) continue
      const tailAt = line.indexOf(tail, start)
      if (tailAt < 0) continue
      const end = tailAt + tail.length - 1
      const range = document.createRange()
      range.setStart(at[start]![0], at[start]![1])
      range.setEnd(at[end]![0], at[end]![1] + 1)
      return range
    }
  }
  return null
}

/** Scroll target for the hit: the card snippet's text centred in the box (a long line
 *  would otherwise park its opening, far from the hit, at the top). A snippet taller than
 *  the box keeps its start in view. Falls back to the highlighted words, then to the
 *  line start. */
function hitTop(box: HTMLElement, matched: HTMLElement): number {
  let rect: { top: number; bottom: number } | null = snippetRange(matched)?.getBoundingClientRect() ?? null
  if (!rect || (!rect.top && !rect.bottom)) {
    const marks = matched.querySelectorAll<HTMLElement>('mark')
    if (!marks.length) return Math.max(0, matched.offsetTop - 2)
    const first = marks[0]!.getBoundingClientRect()
    const last = marks[marks.length - 1]!.getBoundingClientRect()
    rect = { top: Math.min(first.top, last.top), bottom: Math.max(first.bottom, last.bottom) }
  }
  const base = box.getBoundingClientRect().top - box.scrollTop
  const top = rect.top - base
  const bottom = rect.bottom - base
  if (bottom - top > box.clientHeight) return Math.max(0, top - 2)
  return Math.max(0, (top + bottom) / 2 - box.clientHeight / 2)
}

/** Scroll the hit back to the middle of the window (header recenter button).
 *  If the capped window has trimmed the matched line out, reload the seed around it first. */
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
    box.scrollTop = hitTop(box, matched)
    // Refresh saved scrollTop + edge state even if the assignment landed on the
    // current value (no native scroll event fires then).
    box.dispatchEvent(new Event('scroll'))
    return
  }
  box.scrollTo({ top: hitTop(box, matched), behavior: 'smooth' })
}

defineExpose({ recenter })

// ── Word-level links (hover preview + click-through) ─────────────────────────

const paneNavigation = usePaneNavigation()
const booksDataStore = useBooksDataStore()
const {
  wordLinkTooltip,
  closeWordLinkTooltip,
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

// Inline notes (footnotes): hover preview of a marker's note, as in the book view.
const {
  lineNoteTooltip,
  closeLineNoteTooltip,
  keepLineNoteTooltipOpen,
  releaseLineNoteTooltip,
  beginLineNoteTooltipSelection,
} = useLineNotesTooltip(
  boxEl,
  (marker) => {
    const row = marker.closest<HTMLElement>('[data-line-id]')
    return row ? { row, lineId: Number(row.dataset.lineId) } : null
  },
  (lineId) => props.state.lines.find((line) => line.id === lineId)?.notes ?? [],
)
</script>

<template>
  <div dir="rtl" ref="boxEl" class="preview-box" @scroll.passive="onScroll">
    <WordLinkTooltip
      v-if="wordLinkTooltip"
      :key="wordLinkTooltip.id"
      :data="wordLinkTooltip"
      @pointer-enter="keepWordLinkTooltipOpen"
      @pointer-leave="releaseWordLinkTooltip"
      @select-start="beginWordLinkTooltipSelection"
      @close="closeWordLinkTooltip"
    />
    <WordLinkTooltip
      v-if="lineNoteTooltip"
      :key="`line-note-${lineNoteTooltip.id}`"
      :data="lineNoteTooltip"
      @pointer-enter="keepLineNoteTooltipOpen"
      @pointer-leave="releaseLineNoteTooltip"
      @select-start="beginLineNoteTooltipSelection"
      @close="closeLineNoteTooltip"
    />
    <BookViewAbbrevTooltip
      v-if="abbrevTooltip"
      :key="abbrevTooltip.id"
      :data="abbrevTooltip"
    />
    <div v-if="state.loading && !state.lines.length" class="preview-empty ui-chrome">{{ $t('fullTextSearch.loading') }}</div>
    <!-- eslint-disable-next-line vue/no-v-html -->
    <div
      v-for="line in state.lines"
      :key="line.id"
      class="preview-line"
      :data-line-id="line.id"
      :class="{ matched: line.lineIndex === state.lineIndex }"
      :data-matched="line.lineIndex === state.lineIndex ? '' : undefined"
      :style="teamimFontStyle(line.html)"
      v-html="renderHtml(line.html)"
    />
  </div>
</template>

<style scoped>
/* Inline notes (book-view/lines/lineNotes.ts) - same look as the book view. */
.preview-line :deep(sup.footnote-marker),
.preview-line :deep(.line-note-marker) {
  color: var(--accent-color);
  cursor: pointer;
}
.preview-line :deep(.line-note-marker) {
  font-size: 0.72em;
  vertical-align: super;
  line-height: 1;
}
.preview-line :deep(.line-note-marker)::before {
  content: '*';
}
/* A footnote hit's note text, under its source line (useFullTextSearchPreview). */
.preview-line :deep(.preview-note) {
  margin-top: 2px;
  padding-top: 2px;
  border-top: 1px dashed var(--border-color);
  font-size: 0.92em;
}
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
/* The hit line: full ink and an accent edge at the reading start, so it is found at a
   glance among the muted context lines however far the window has been scrolled. */
.preview-line.matched {
  color: var(--text-primary);
  border-inline-start: 3px solid var(--accent-color);
  padding-inline-start: 6px;
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
/* Muted by opacity — matches the reading views. All markers share the same color
   (--text-secondary fallback). */
.preview-line :deep(.word-link-marker) {
  font-size: 0.6em;
  line-height: 1;
  color: var(--wl-marker-color, var(--text-secondary));
  opacity: 0.65;
  cursor: pointer;
  font-style: normal;
  font-weight: var(--wl-marker-weight, normal);
  text-decoration: var(--wl-marker-decoration, none);
  text-underline-offset: 2px;
}
/* Label via CSS content — the marker must contribute zero text characters. */
.preview-line :deep(.word-link-marker)::before {
  content: var(--wl-marker-open, '') attr(data-wl-label) var(--wl-marker-close, '');
}
.preview-empty {
  color: var(--text-secondary);
  opacity: 0.7;
  font-size: 0.9em;
}
</style>
