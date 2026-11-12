<script lang="ts">
/** Full snippet HTML -> the trimmed HTML a mounted row shows, so copy takes what is on
 *  screen. Only mounted, trimmed rows are in it; each removes itself on unmount. */
const shownByHtml = new Map<string, { shown: string; rows: number }>()
export function shownSnippetHtml(html: string): string {
  return shownByHtml.get(html)?.shown ?? html
}

/** Rows waiting to be re-fitted. All of them are fitted together, once per frame: during a
 *  window resize or while results stream in, many rows ask at once, and fitting each one on
 *  its own callback forced a layout per row and made row heights bounce, which the scroller
 *  then corrected with a visible jump. */
const pendingFits = new Set<{ prepare: () => void; finish: () => void }>()
let fitFrame = 0
function scheduleFit(row: { prepare: () => void; finish: () => void }) {
  pendingFits.add(row)
  if (fitFrame) return
  fitFrame = requestAnimationFrame(() => {
    fitFrame = 0
    const rows = [...pendingFits]
    pendingFits.clear()
    // Every write first, then every read: one layout for the whole batch instead of one per row.
    for (const row of rows) row.prepare()
    for (const row of rows) row.finish()
  })
}
function cancelFit(row: { prepare: () => void; finish: () => void }) {
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

/** True when the clamp cuts the text and the first match sits past the middle of the
 *  visible lines - so there is more context before it than after it, or it is cut off.
 *  Text that is not cut shows everything after the match already: nothing to balance. */
function matchPastMiddle(): boolean {
  const box = el.value
  if (!box) return false
  const marks = firstMatchMarks(box)
  if (!marks.length) return false
  if (box.scrollHeight <= box.clientHeight + 1) return false
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
  if (!firstRect || !lastRect) return false
  const middle = (readingPos(firstRect, false) + readingPos(lastRect, true)) / 2
  return middle > 0.5
}

/** `html` with its first `drop` leading words (before the first <mark>) replaced by "…". */
function trimmed(lead: string[], drop: number, rest: string): string {
  return '…' + lead.slice(drop).join(' ') + (drop < lead.length ? ' ' : '') + rest
}

/** Leading words dropped for the current html and width; 0 = full snippet shown. */
let dropped = 0
/** The width the current trim was fitted at; -1 = never fitted. */
let fittedWidth = -1
/** Write phase. A wider row (or new html) may now fit more words, so it starts over from
 *  the full snippet. A narrower row keeps its trim: it can only need to drop more words. */
function prepare() {
  const box = el.value
  if (!box) return
  const width = box.clientWidth
  const fromScratch = fittedWidth < 0 || width > fittedWidth
  fittedWidth = width
  if (fromScratch && dropped !== 0) {
    dropped = 0
    shown.value = props.html
    box.innerHTML = props.html
    unregister()
  }
}

/** Read phase (plus a binary search for rows whose mark is hidden). */
function finish() {
  const box = el.value
  if (!box || !matchPastMiddle()) return
  const markAt = props.html.indexOf('<mark>')
  if (markAt < 0) return
  const lead = props.html.slice(0, markAt).trim().replace(/^…\s*/, '').split(/\s+/).filter(Boolean)
  const rest = props.html.slice(markAt)
  // Smallest number of dropped words that brings the match to the middle of the visible text.
  let lo = Math.min(lead.length, Math.max(1, dropped + 1)), hi = lead.length
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    box.innerHTML = trimmed(lead, mid, rest)
    if (matchPastMiddle()) lo = mid + 1
    else hi = mid
  }
  dropped = lo
  shown.value = trimmed(lead, lo, rest)
  box.innerHTML = shown.value
  register(props.html, shown.value)
}

const fitter = { prepare, finish }

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
