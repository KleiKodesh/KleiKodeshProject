/**
 * Tab stops laid out as Word (.docx / .doc / RTF) and LibreOffice (.odt) lay them out, with the browser's text metrics - what the
 * library's HTML writer cannot know. The library marks each paragraph that needs it with `data-tabs` (FastDocView
 * Html/WordParagraphFormatting.TabStopsAttribute: default interval, start indent, first-line indent, relative-to-indent flag, then
 * the stops in pt with alignment and leader) and each absolute position tab (.docx w:ptab) with `data-ptab`.
 *
 * Per paragraph, after its block is rendered: every tab character is wrapped in a `.tab-space` span (the text keeps its tab, so
 * search, copy and the document text are unchanged; an inline span, so the tab keeps its bidi class, a segment separator), all
 * spans are measured at zero width in one layout, and each one gets the width (end padding) that brings the text after it to its stop. The rules are LibreOffice's (sw/source/core/text/txttab.cxx, master read
 * 2026-11-17):
 *
 * - SwTextFormatter::NewTabPortion: the first custom stop past the current position; none -> the next default stop (multiples of
 *   the interval from the margin, or from the indent when tabs count from it), at least 50 twips away outside Word compatibility
 *   (TAB_COMPAT); inside the hanging indent the start indent itself is a stop when no custom stop comes before it; a non-left stop
 *   past the right edge acts as a right stop at the edge.
 * - SwTabPortion::PostFormat: right -> the text up to the next tab ends at the stop; centre -> centred on it (kept inside the line);
 *   decimal -> the first decimal point sits on it (no point: as a right stop); a text too wide leaves the tab no width.
 * - Leaders fill the tab's width (a pseudo-element, not text); bar stops draw a vertical line through the paragraph and take no
 *   tab (Word draws them; LibreOffice ignores them, ww8par.cxx Read_Tab).
 *
 * All positions are in the paragraph's own coordinates (pt), so zooming does not need a new pass; a width change re-runs it
 * (line breaks move). A block that is not shown is never touched.
 */

type Stop = { position: number; alignment: string; leader: string }
type Data = { interval: number; start: number; firstLine: number; fromIndent: boolean; stops: Stop[] }
type Tab = { span: HTMLElement; ptab: string[] | null }
type Paragraph = { element: HTMLElement; data: Data; tabs: Tab[]; rightToLeft: boolean }

const PARAGRAPH_SELECTOR = 'p, h1, h2, h3, h4, h5, h6, .word-paragraph-block, .text-box-paragraph'
const OWNER_SELECTOR = `${PARAGRAPH_SELECTOR}, td, .list-marker, .text-box, .word-shape`
const DEFAULT_DATA: Data = { interval: 36, start: 0, firstLine: 0, fromIndent: false, stops: [] }

export function parseTabData(value: string | undefined): Data {
  if (!value) return DEFAULT_DATA
  const [head = '', ...stops] = value.split(';')
  const [interval = 36, start = 0, firstLine = 0, fromIndent = 0] = head.split(' ').map(Number)
  return {
    interval: interval > 0 ? interval : 36, start, firstLine, fromIndent: fromIndent === 1,
    stops: stops.map((stop) => { const [position = '0', alignment = 'l', leader = ''] = stop.split(' '); return { position: Number(position), alignment, leader } })
      .filter((stop) => Number.isFinite(stop.position)),
  }
}

/**
 * The stop a tab at `x` (pt from the tab origin) goes to: LibreOffice's NewTabPortion. `right` is the text area's end (pt from
 * the origin). Returns the stop's position and alignment (a default or hanging-indent stop is left-aligned, without a leader).
 */
export function nextStop(data: Data, x: number, right: number): Stop {
  const stop = data.stops.find((s) => s.alignment !== 'b' && s.position > x + 0.01)
  // inside the hanging indent the start indent is a stop when no custom stop comes first (txttab.cxx #i115705)
  if (x < data.start - 0.01 && (!stop || stop.position > data.start)) return { position: data.start, alignment: 'l', leader: '' }
  if (stop) return stop.position > right && stop.alignment !== 'l' ? { ...stop, position: right, alignment: 'r' } : stop
  const origin = data.fromIndent ? data.start : 0
  const minimum = data.fromIndent ? 2.5 : 0                // 50 twips outside TAB_COMPAT (OpenDocument), none for Word
  const steps = Math.floor((x - origin) / data.interval)
  let position = origin + (steps + 1) * data.interval
  if (position <= x + minimum) position += data.interval
  return { position, alignment: 'l', leader: '' }
}

/** The tab's width (pt) for a tab at `x` going to `stop`, with `segment` the text after it up to the next tab and `decimal` the part before its decimal point. */
export function tabWidth(stop: Stop, x: number, segment: number, decimal: number | null, right: number): number {
  let width: number
  switch (stop.alignment) {
    case 'r': width = stop.position - x - segment; break
    case 'c': width = stop.position + segment / 2 > right ? right - x - segment : stop.position - x - segment / 2; break
    case 'd': width = stop.position - x - (decimal ?? segment); break
    default: return Math.max(0, Math.min(stop.position, Math.max(right, x)) - x)
  }
  // text that ends at the line's end must not wrap on a rounding: a hair short of it
  return Math.max(0, width - 0.05)
}

function ownTabs(paragraph: HTMLElement): Tab[] {
  const owns = (node: Node) => (node.parentElement?.closest(OWNER_SELECTOR) ?? null) === paragraph
  const walker = document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT)
  const texts: Text[] = []
  for (let node = walker.nextNode(); node; node = walker.nextNode())
    if ((node.nodeValue ?? '').includes('\t') && !node.parentElement?.classList.contains('tab-space') && owns(node)) texts.push(node as Text)
  for (const text of texts) {
    const parts = (text.nodeValue ?? '').split('\t')
    const fragment = document.createDocumentFragment()
    parts.forEach((part, i) => {
      if (i > 0) { const span = document.createElement('span'); span.className = 'tab-space'; span.textContent = '\t'; fragment.appendChild(span) }
      if (part) fragment.appendChild(document.createTextNode(part))
    })
    text.replaceWith(fragment)
  }
  return [...paragraph.querySelectorAll<HTMLElement>('.tab-space')].filter(owns)
    .map((span) => ({ span, ptab: span.parentElement?.closest<HTMLElement>('[data-ptab]')?.dataset.ptab?.split(' ') ?? null }))
}

/** Width of the part of `range` on the line of `line` (px), from its first to its last box there. */
function widthOnLine(range: Range, line: DOMRect): number {
  let left = Infinity, right = -Infinity
  for (const box of range.getClientRects()) {
    if (box.bottom <= line.top + 1 || box.top >= line.bottom - 1) continue
    left = Math.min(left, box.left); right = Math.max(right, box.right)
  }
  return right > left ? right - left : 0
}

// Not the run text after a tab: what sits inside the paragraph's text but is not part of it (note marks, fields shown as
// placeholders, shapes and text boxes, list labels, nested cells, equations) - a decimal point there does not count
const NOT_RUN_TEXT = '.note-reference, .note-mark, .field-page-placeholder, .text-box, .word-shape, .word-picture-missing, .list-marker, td, math, sup, sub'

/** The text after `tab` up to the next tab (or the paragraph's end), and the part of it before its first decimal point in the run text (null: none). */
function segmentRanges(paragraph: HTMLElement, tab: HTMLElement, next: HTMLElement | undefined): { segment: Range; decimal: Range | null } {
  const segment = document.createRange()
  segment.setStartAfter(tab)
  if (next) segment.setEndBefore(next); else segment.setEnd(paragraph, paragraph.childNodes.length)
  let decimal: Range | null = null
  const walker = document.createTreeWalker(segment.commonAncestorContainer, NodeFilter.SHOW_TEXT)
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (!segment.intersectsNode(node)) continue
    const nested = node.parentElement?.closest(NOT_RUN_TEXT)
    if (nested && paragraph.contains(nested)) continue
    const at = (node.nodeValue ?? '').indexOf('.')
    if (at < 0) continue
    decimal = document.createRange()
    decimal.setStartAfter(tab)
    decimal.setEnd(node, at)
    break
  }
  return { segment, decimal }
}

/**
 * Bar stops: a vertical line through the paragraph at the stop. Measured like the tabs, from the text's start edge less the
 * start indent: a paragraph border's line and space sit inside the box here (Word draws them in the indent), so the line moves
 * in with the text by the box's start padding and border.
 */
function drawBars(paragraph: Paragraph) {
  const { element, data } = paragraph
  for (const old of element.querySelectorAll(':scope > .tab-bar')) old.remove()
  const bars = data.stops.filter((s) => s.alignment === 'b')
  if (!bars.length) return
  const style = getComputedStyle(element)
  if (style.position === 'static') element.style.position = 'relative'
  // the bar's inset counts from the padding box; the text starts after the padding (CSS px -> pt: 3/4)
  const padding = parseFloat(style.direction === 'rtl' ? style.paddingRight : style.paddingLeft) * 0.75
  for (const bar of bars) {
    const line = document.createElement('span')
    line.className = 'tab-bar'
    line.style.insetInlineStart = `${+(bar.position - data.start + padding).toFixed(2)}pt`
    element.appendChild(line)
  }
}

type Job = { paragraph: Paragraph; from: number; wrapped?: number }

/**
 * Lays out the tabs of every marked paragraph inside `root`: wrap and zero (writes), measure (one layout), set widths (writes),
 * then once more for each paragraph where a tab or its text landed on another line than it was measured on - from that tab
 * on, measured on its new line, as Word measures a tab on the line it ends up on. Each round fixes at least one more tab (a
 * tab's own place depends only on what comes before it), so it ends after at most one round per tab.
 */
export function layoutTabs(root: Element) {
  const paragraphs: Paragraph[] = []
  const candidates = new Set<HTMLElement>(root.querySelectorAll<HTMLElement>('[data-tabs]'))
  for (const ptab of root.querySelectorAll<HTMLElement>('[data-ptab]')) { const owner = ptab.closest<HTMLElement>(PARAGRAPH_SELECTOR); if (owner) candidates.add(owner) }
  for (const element of candidates) {
    const data = parseTabData(element.dataset.tabs)
    const hasTab = (element.textContent ?? '').includes('\t')
    const paragraph: Paragraph = { element, data, tabs: hasTab ? ownTabs(element) : [], rightToLeft: false }
    if (data.stops.some((s) => s.alignment === 'b')) drawBars(paragraph)
    if (!paragraph.tabs.length) continue
    paragraphs.push(paragraph)
  }
  if (!paragraphs.length) return

  let jobs: Job[] = paragraphs.map((paragraph) => ({ paragraph, from: 0 }))
  let pixelsPerPoint = 0
  const rounds = Math.max(...paragraphs.map((p) => p.tabs.length)) + 1
  for (let round = 0; round < rounds && jobs.length; round++) {
    for (const { paragraph, from, wrapped } of jobs)
      paragraph.tabs.slice(from).forEach(({ span }, k) => {
        span.style.paddingInlineEnd = '0px'
        delete span.dataset.leader
        const previous = span.previousSibling
        if (from + k !== wrapped && previous instanceof HTMLBRElement && previous.classList.contains('tab-break')) previous.remove()
      })
    // one layout: the scale (pt -> px, zoom included) from a probe, then every tab and its text
    if (!pixelsPerPoint) {
      const probe = jobs[0]!.paragraph.tabs[0]!.span
      probe.style.paddingInlineEnd = '100pt'
      pixelsPerPoint = probe.getBoundingClientRect().width / 100 || 4 / 3
      probe.style.paddingInlineEnd = '0px'
    }
    const results = jobs.map((job) => planTabs(job, pixelsPerPoint))
    for (const result of results)
      for (const { span, width, leader, wraps } of result) {
        // a left stop past the line's end: the tab goes to the next line (txttab.cxx PreFormat bFull) - a break before it
        const previous = span.previousSibling
        if (wraps && !(previous instanceof HTMLBRElement && previous.classList.contains('tab-break'))) {
          const lineBreak = document.createElement('br'); lineBreak.className = 'tab-break'; span.before(lineBreak)
        }
        span.style.paddingInlineEnd = `${Math.floor(width * 100) / 100}pt`
        if (leader) span.dataset.leader = leader
      }
    // where a set width sent a tab or its text to another line, or a tab went to the next line, it is laid out again there
    const next: Job[] = []
    results.forEach((result, j) => {
      const { paragraph, from } = jobs[j]!
      const wrapped = result.findIndex((r) => r.wraps)
      if (wrapped === 0 && jobs[j]!.wrapped === from) return     // already on its own line: no stop fits there either
      if (wrapped >= 0) { next.push({ paragraph, from: from + wrapped, wrapped: from + wrapped }); return }
      const moved = result.findIndex(({ span, line }, k) => k > 0 && Math.abs(span.getBoundingClientRect().top - line) > 1)
      if (moved > 0) next.push({ paragraph, from: from + moved })
    })
    jobs = next
  }
}

/** Widths of the job's tabs (from its first one on; the ones before keep theirs), from one measuring pass - reads only. */
function planTabs({ paragraph, from }: Job, pixelsPerPoint: number) {
  const { element, data, tabs } = paragraph
  const style = getComputedStyle(element)
  paragraph.rightToLeft = style.direction === 'rtl'
  const box = element.getBoundingClientRect()
  const contentStart = paragraph.rightToLeft ? box.right - parseFloat(style.paddingRight) - parseFloat(style.borderRightWidth)
                                             : box.left + parseFloat(style.paddingLeft) + parseFloat(style.borderLeftWidth)
  const contentEnd = paragraph.rightToLeft ? box.left + parseFloat(style.paddingLeft) + parseFloat(style.borderLeftWidth)
                                           : box.right - parseFloat(style.paddingRight) - parseFloat(style.borderRightWidth)
  const toPoints = (x: number) => (paragraph.rightToLeft ? contentStart - x : x - contentStart) / pixelsPerPoint + data.start
  const right = toPoints(contentEnd)
  // widths, line by line (a tab moves everything after it on its line); positions read live, so earlier tabs count as set
  let shift = 0, lineTop = NaN
  const lineStart = data.start + Math.max(0, data.firstLine) + 0.5
  const planned: { span: HTMLElement; line: number; width: number; leader: string; wraps: boolean }[] = []
  for (let i = from; i < tabs.length; i++) {
    const tab = tabs[i]!
    const line = tab.span.getBoundingClientRect()
    if (Math.abs(line.top - lineTop) > 1) { shift = 0; lineTop = line.top }
    const x = toPoints(paragraph.rightToLeft ? line.right : line.left) + shift
    // the text after the tab is measured only for a stop that needs it (right / centre / decimal)
    let ranges: ReturnType<typeof segmentRanges> | null = null
    const measure = () => (ranges ??= segmentRanges(element, tab.span, tabs[i + 1]?.span))
    let stop: Stop
    if (tab.ptab) {
      const [alignment = 'l', leader = '-', relativeTo = 'm'] = tab.ptab
      const start = relativeTo === 'i' ? data.start : 0
      stop = { position: alignment === 'r' ? right : alignment === 'c' ? (start + right) / 2 : start, alignment, leader: leader === '-' ? '' : leader }
    } else stop = nextStop(data, x, right)
    if (!tab.ptab && stop.alignment === 'l' && stop.position >= right - 0.01 && x > lineStart) {   // bFull: Width() <= X + PrtWidth()
      planned.push({ span: tab.span, line: line.top, width: 0, leader: '', wraps: true })
      break                                    // the rest is laid out after the break, on the next line
    }
    const segment = stop.alignment === 'l' ? 0 : widthOnLine(measure().segment, line) / pixelsPerPoint
    const decimalRange = stop.alignment === 'd' ? measure().decimal : null
    const decimal = decimalRange ? widthOnLine(decimalRange, line) / pixelsPerPoint : null
    const width = tabWidth(stop, x, segment, decimal, right)
    shift += width
    planned.push({ span: tab.span, line: line.top, width, leader: width > 0 ? stop.leader : '', wraps: false })
  }
  return planned
}

/** `v-fast-doc-view-tabs` on the element that holds block HTML: lays out its tabs after each render and when its width changes. */
export const vFastDocViewTabs = {
  mounted(element: HTMLElement) { run(element) },
  updated(element: HTMLElement) { run(element) },
  unmounted(element: HTMLElement) { widthObserver.unobserve(element); widths.delete(element) },
}

function run(element: HTMLElement) {
  if (!element.querySelector('[data-tabs], [data-ptab]')) return
  layoutTabs(element)
  watchWidth(element)
  // web fonts still loading change every width: once more when they are in
  if (document.fonts?.status === 'loading') document.fonts.ready.then(() => { if (element.isConnected) layoutTabs(element) })
}

const widths = new WeakMap<Element, number>()
const pending = new Set<HTMLElement>()
let frame = 0
// a width change moves line breaks; the pass runs in the next frame (inside the observer callback a height change of the
// scroller's rows would be an observer loop)
const widthObserver = new ResizeObserver((entries) => {
  for (const entry of entries) {
    const width = Math.round(entry.contentRect.width)
    if (widths.get(entry.target) === width) continue
    widths.set(entry.target, width)
    pending.add(entry.target as HTMLElement)
  }
  if (pending.size && !frame) frame = requestAnimationFrame(() => {
    frame = 0
    for (const element of pending) if (element.isConnected) run(element)
    pending.clear()
  })
})

/** Watches a block that has tabs; its width now is the one the pass just used (a change before the first observation counts too). */
function watchWidth(element: HTMLElement) {
  if (widths.has(element)) return
  const style = getComputedStyle(element)          // the content box, as the observer's contentRect measures it
  widths.set(element, Math.round(element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)))
  widthObserver.observe(element)
}
