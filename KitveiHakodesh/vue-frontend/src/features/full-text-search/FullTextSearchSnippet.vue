<script lang="ts">
/** Full snippet HTML -> the trimmed HTML a mounted row shows, so copy takes what is on
 *  screen. Only mounted, trimmed rows are in it; each removes itself on unmount. */
const shownByHtml = new Map<string, { shown: string; rows: number }>()
export function shownSnippetHtml(html: string): string {
  return shownByHtml.get(html)?.shown ?? html
}

/** One row's fitting steps. Each step is ONLY reads or ONLY writes, so the batch below can
 *  run a step for every row before the next step: one layout per step for all rows. */
interface SnippetFitter {
  /** Read: the row's width now. */
  measureWidth(): void
  /** Write: back to the full snippet when the row got wider (or is new). */
  resetIfWider(): void
  /** Read: true when the match needs centering, and the search over leading words begins.
   *  Its first guess is an estimate; most rows need no other. */
  startSearch(): boolean
  /** Write: show the next guess. False once the search has its answer. */
  writeGuess(): boolean
  /** Read: was that guess enough? Narrows the search. */
  readGuess(): void
  /** Write: show the answer. */
  commit(): void
}

/** Rows waiting to be re-fitted. All of them are fitted together, once per frame: during a
 *  window resize or while results stream in, many rows ask at once. Fitting each on its own
 *  forced several layouts per row and made row heights bounce, which the scroller then
 *  corrected with a visible jump. In lockstep, every row's binary search takes its next
 *  step together, so a frame costs about log2(words) layouts however many rows there are. */
const pendingFits = new Set<SnippetFitter>()
let fitFrame = 0
function scheduleFit(row: SnippetFitter) {
  pendingFits.add(row)
  if (fitFrame) return
  fitFrame = requestAnimationFrame(() => {
    fitFrame = 0
    const rows = [...pendingFits]
    pendingFits.clear()
    for (const row of rows) row.measureWidth()
    for (const row of rows) row.resetIfWider()
    const searching = rows.filter(row => row.startSearch())
    for (;;) {
      const guessing = searching.filter(row => row.writeGuess())
      if (!guessing.length) break
      for (const row of guessing) row.readGuess()
    }
    for (const row of searching) row.commit()
  })
}
function cancelFit(row: SnippetFitter) {
  pendingFits.delete(row)
}
</script>

<script setup lang="ts">
/**
 * One result's clamped snippet, with the first match ALWAYS visible and centered.
 *
 * The snippet is a fixed window of context words on each side of the match (the search's
 * "context words" setting). In a narrow pane the leading words alone can fill the clamped
 * lines and push the match to the end, or past it. So after layout, if the clamp cuts the
 * text and the match sits past the middle of the visible text, leading words are dropped
 * (binary search over the text before the match, so a handful of reflows at most) until it
 * is in the middle, and a "…" takes their place. Rows that need nothing cost one rect read.
 */
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { useResizeObserver } from '@vueuse/core'

const props = defineProps<{ html: string }>()

const el = ref<HTMLElement | null>(null)
/** The HTML actually shown — the full snippet, or one with leading words trimmed. */
const shown = shallowRef(props.html)
/** The full HTML this row registered a trimmed version for — rows can share one snippet
 *  (the same text in two books), so entries are counted per row. */
let registered: string | null = null

function unregister() {
  if (registered === null) return
  const entry = shownByHtml.get(registered)
  if (entry && --entry.rows <= 0) shownByHtml.delete(registered)
  registered = null
}

function register(html: string, trimmedHtml: string) {
  unregister()
  const entry = shownByHtml.get(html)
  if (entry) { entry.shown = trimmedHtml; entry.rows++ }
  else shownByHtml.set(html, { shown: trimmedHtml, rows: 1 })
  registered = html
}

/** The first match: its first <mark> plus the marks right after it with only spaces
 *  between (a phrase query marks each word on its own). */
function firstMatchMarks(box: HTMLElement): HTMLElement[] {
  const first = box.querySelector('mark')
  if (!first) return []
  const marks: HTMLElement[] = [first]
  let node = first.nextSibling
  while (node) {
    if (node.nodeType === Node.TEXT_NODE && !node.textContent!.trim()) { node = node.nextSibling; continue }
    if (node instanceof HTMLElement && node.tagName === 'MARK') { marks.push(node); node = node.nextSibling; continue }
    break
  }
  return marks
}

/** Where the first match sits in the visible text, as reading positions (0 = start of the
 *  first line, 1 = end of the last): `start` of the match and its `middle`. Null when the
 *  clamp does not cut the text - then everything after the match shows already, nothing to
 *  balance. */
function matchPosition(): { start: number; middle: number } | null {
  const box = el.value
  if (!box) return null
  const marks = firstMatchMarks(box)
  if (!marks.length) return null
  if (box.scrollHeight <= box.clientHeight + 1) return null
  const boxRect = box.getBoundingClientRect()
  const style = getComputedStyle(box)
  const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.5
  const lines = Math.max(1, Math.round(box.clientHeight / lineHeight))
  const rtl = style.direction === 'rtl'
  // Reading position of a point in the visible text: 0 = start of line 1, 1 = end of the last line.
  const readingPos = (rect: DOMRect, atEnd: boolean) => {
    const line = Math.floor((rect.top + rect.height / 2 - boxRect.top) / lineHeight)
    const x = rtl ? (atEnd ? rect.left : rect.right) : (atEnd ? rect.right : rect.left)
    const along = rtl ? (boxRect.right - x) / boxRect.width : (x - boxRect.left) / boxRect.width
    return (line + along) / lines
  }
  const firstRect = marks[0]!.getClientRects()[0]
  const lastRects = marks[marks.length - 1]!.getClientRects()
  const lastRect = lastRects[lastRects.length - 1]
  if (!firstRect || !lastRect) return null
  const start = readingPos(firstRect, false)
  return { start, middle: (start + readingPos(lastRect, true)) / 2 }
}

/** Past the middle: more context before the match than after it, or the match cut off. */
const PAST_MIDDLE = 0.5
/** A guess that lands the match between this and the middle is good enough: no search. */
const CLOSE_ENOUGH = 0.38

/** `html` with its first `drop` leading words (before the first <mark>) replaced by "…". */
function trimmed(lead: string[], drop: number, rest: string): string {
  return '…' + lead.slice(drop).join(' ') + (drop < lead.length ? ' ' : '') + rest
}

/** Leading words dropped for the current html and width; 0 = full snippet shown. */
let dropped = 0
/** The width the current trim was fitted at; -1 = never fitted. */
let fittedWidth = -1
/** The width read by measureWidth for this batch. */
let measuredWidth = -1
/** The running search: smallest number of dropped words that centers the match. The first
 *  guess is ESTIMATED from where the match sits (words before it per unit of reading
 *  position), so most rows land in one try; a miss narrows a binary search instead. A fast,
 *  wide resize re-fits every cut row each frame, so tries are what a frame costs. */
let lead: string[] = []
let rest = ''
let lo = 0, hi = 0, guess = 0
let estimate: number | null = null

const fitter: SnippetFitter = {
  measureWidth() {
    measuredWidth = el.value?.clientWidth ?? -1
  },
  // A wider row (or new html) may now fit more words, so it starts over from the full
  // snippet. A narrower row keeps its trim: it can only need to drop more words.
  resetIfWider() {
    const box = el.value
    if (!box) return
    const fromScratch = fittedWidth < 0 || measuredWidth > fittedWidth
    fittedWidth = measuredWidth
    if (fromScratch && dropped !== 0) {
      dropped = 0
      shown.value = props.html
      box.innerHTML = props.html
      unregister()
    }
  },
  startSearch() {
    if (!el.value) return false
    const at = matchPosition()
    if (!at || at.middle <= PAST_MIDDLE) return false
    const markAt = props.html.indexOf('<mark>')
    if (markAt < 0) return false
    lead = props.html.slice(0, markAt).trim().replace(/^…\s*/, '').split(/\s+/).filter(Boolean)
    rest = props.html.slice(markAt)
    // Nothing left to drop (the text starts with the match, or every word is dropped already).
    if (lead.length <= dropped) return false
    lo = Math.min(lead.length, Math.max(1, dropped + 1))
    hi = lead.length
    const wordsBefore = lead.length - dropped
    const wordsPerPos = wordsBefore / Math.max(0.05, at.start)
    estimate = Math.min(hi, Math.max(lo, dropped + Math.ceil((at.middle - PAST_MIDDLE) * wordsPerPos)))
    return true
  },
  writeGuess() {
    const box = el.value
    if (!box || lo >= hi) return false
    guess = estimate ?? (lo + hi) >> 1
    estimate = null
    box.innerHTML = trimmed(lead, guess, rest)
    return true
  },
  readGuess() {
    const at = matchPosition()
    if (at && at.middle > PAST_MIDDLE) { lo = Math.min(guess + 1, hi); return }
    hi = guess
    // Centered, or the text is no longer cut: a guess this close ends the search.
    if (!at || at.middle >= CLOSE_ENOUGH) lo = guess
  },
  commit() {
    const box = el.value
    if (!box) return
    dropped = lo
    shown.value = trimmed(lead, lo, rest)
    box.innerHTML = shown.value
    register(props.html, shown.value)
  },
}

watch(() => props.html, () => {
  // New text: forget the old trim and fit from scratch.
  dropped = 0
  fittedWidth = -1
  shown.value = props.html
  if (el.value) el.value.innerHTML = props.html
  unregister()
  scheduleFit(fitter)
}, { flush: 'post' })
onBeforeUnmount(() => { cancelFit(fitter); unregister() })
// Fires once on mount and on every size change of the row; only a width change matters
// (a height change is this row's own trim, or the clamp, and must not loop back here).
useResizeObserver(el, () => {
  const width = el.value?.clientWidth ?? 0
  if (width === fittedWidth) return
  scheduleFit(fitter)
})
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html -->
  <div ref="el" class="snippet" v-html="shown" />
</template>

<style scoped>
.snippet {
  font-family: var(--text-font);
  font-size: 1em;
  line-height: var(--line-height, 1.5);
  color: var(--text-secondary);
  direction: rtl;
  text-align: justify;
  text-align-last: right;
  user-select: text;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.snippet :deep(.match),
.snippet :deep(mark) {
  background: transparent;
  color: var(--accent-color);
  font-weight: 600;
  user-select: text;
}
</style>
