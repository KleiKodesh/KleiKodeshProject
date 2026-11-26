/**
 * Equations finished with the browser's layout - what the library's HTML writer cannot know (FastDocView
 * Html/WordOfficeMathHtml.cs). Measurement is batched for all the blocks a render holds: writes, then one read of every
 * block and operator, never a read per operator (layoutMath: at most three reads per render, five with a matrix).
 *
 * - Matrices (`mtable.omath-matrix`): rows one math line apart as Word sets them (matrixRowShifts), not by the font's box.
 * - Stretched operators (`mo.omath-stretch[data-variants]`): Word draws a grown delimiter or n-ary operator with the
 *   font's OpenType MATH size variants, else its assembly; the operator carries them ("height:width ... |assembly width", em).
 *   Word picks the smallest variant covering TeX's size for the content's ink (DELIMITER_FACTOR below; else the assembly) -
 *   the operator is narrowed to that variant's width by equal negative margins (Chromium stops stretching inside an mpadded).
 * - Display equations cut into pieces at their operators (`.omath-para`):
 *   - `data-math-break="repeat"` (m:brkBin repeat, ECMA-376 22.1.2.15): a piece that starts a line shows its hidden operator
 *     copy (`mo.omath-repeat`); one with `data-line-end` (brkBinSub "+-", 22.1.2.16) makes the line before end with that sign.
 *   - `data-math-wrap-right` (m:wrapRight, 22.1.2.121): continuation lines at the right edge, the first line kept where its
 *     alignment puts it.
 *   The lines are read from the pieces' boxes; the copies are shown at the line starts and the lines read again (a shown
 *   copy can move a later break) until they settle (two more reads at most).
 */

const STRETCH = 'mo.omath-stretch[data-variants]'
const BLOCKS = '.omath-para[data-math-break], .omath-para[data-math-wrap-right]'
const MATRICES = 'mtable.omath-matrix'

/**
 * TeX's \left / \right size (tex.web make_left_right, as KaTeX src/delimiter.ts makeLeftRightDelim takes it): with d the
 * larger distance of the content's ink from the math axis, the delimiter must cover max(2d x 901/1000, 2d - 5 pt at 10 pt,
 * i.e. 0.5 em). Word's PDFs (test-data/word-math/brackets-*-v1.pdf) follow it for delimiters and for growing n-ary operators.
 */
const DELIMITER_FACTOR = 0.901, DELIMITER_SHORTFALL_EM = 0.5

let canvas: CanvasRenderingContext2D | null | undefined
const inkCache = new Map<string, { ascent: number; descent: number; fontAscent: number; fontDescent: number }>()

/** A token's ink above / below its baseline and its font's ascent / descent (px), from the browser's text metrics. */
function inkOf(text: string, font: string) {
  const key = font + '|' + text
  let ink = inkCache.get(key)
  if (ink) return ink
  canvas ??= document.createElement('canvas').getContext('2d')
  if (!canvas) return null
  canvas.font = font
  const m = canvas.measureText(text)
  ink = { ascent: m.actualBoundingBoxAscent, descent: m.actualBoundingBoxDescent, fontAscent: m.fontBoundingBoxAscent, fontDescent: m.fontBoundingBoxDescent }
  if (inkCache.size > 2000) inkCache.clear()
  inkCache.set(key, ink)
  return ink
}

/** The content a stretched operator grows with: a delimiter's row (without its fences), an n-ary operator's argument. */
function contentOf(operator: HTMLElement): Element | null {
  if (operator.getAttribute('data-stretch') !== 'nary') return operator.parentElement
  let wrapper: Element = operator
  while (wrapper.parentElement && !['mrow', 'math'].includes(wrapper.parentElement.tagName.toLowerCase())) wrapper = wrapper.parentElement
  return wrapper.nextElementSibling
}

const INK_TOKENS = 'mi, mn, mo:not(.omath-stretch), mtext'

/** A token's ink (px, top / bottom) and baseline from its box and the browser's text metrics; null for no ink. */
function tokenInk(token: HTMLElement): { top: number; bottom: number; baseline: number } | null {
  const text = token.textContent ?? ''
  if (!text.trim() || token.closest('mphantom')) return null
  const box = token.getBoundingClientRect()
  if (!(box.height > 0)) return null
  const style = getComputedStyle(token)
  const ink = inkOf(text, `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`)
  if (!ink) return null
  // a token box is either the font's line box or the ink box: its baseline sits accordingly
  const lineBox = ink.fontAscent + ink.fontDescent
  if (!(lineBox > 0) || !Number.isFinite(ink.ascent) || !Number.isFinite(ink.descent)) return null
  const baseline = Math.abs(box.height - lineBox) < Math.abs(box.height - (ink.ascent + ink.descent)) ? box.top + ink.fontAscent * box.height / lineBox : box.top + ink.ascent
  return { top: baseline - ink.ascent, bottom: baseline + ink.descent, baseline }
}

/** The content's ink extent (px, top / bottom) from its tokens' boxes and text metrics. */
function inkExtent(content: Element): { top: number; bottom: number } | null {
  let top = Infinity, bottom = -Infinity
  for (const token of content.querySelectorAll<HTMLElement>(INK_TOKENS)) {
    const ink = tokenInk(token)
    if (!ink) continue
    top = Math.min(top, ink.top)
    bottom = Math.max(bottom, ink.bottom)
  }
  return top < bottom ? { top, bottom } : null
}

/**
 * The ink height (px) Word gives a matrix that is all of the content's ink, centred on the axis as Word centres it: its rows one
 * math line apart (the library's `mtr` height, the font's line height - Word's pitch; Chromium spaces them by the font's box,
 * ~1.32 em in Cambria Math, which put the 3-row matrix's ink past Word's variant), a row taller than that kept apart by its own ink,
 * from the first row's ink above its baseline to the last row's below. Word 16 (test-data/word-math/brackets-cambria-v1.pdf):
 * 3 rows of "1x" are 3.02 em of ink, centred on the delimiter within 0.27 pt. Null when the content has other ink.
 */
function matrixInkHeight(content: Element): number | null {
  const tables = content.matches('mtable') ? [content] : [...content.querySelectorAll(':scope > mtable, :scope > mrow > mtable')]
  if (tables.length !== 1) return null
  const table = tables[0]!
  const tokens = [...content.querySelectorAll<HTMLElement>(INK_TOKENS)]
  if (tokens.some(token => !table.contains(token) && tokenInk(token))) return null
  const rows = matrixRows(table)
  if (!rows || rows.inks.length < 2 || rows.inks.some(ink => !ink)) return null
  const inks = rows.inks as RowInk[]
  let height = inks[0]!.above + inks[inks.length - 1]!.below
  for (let i = 0; i + 1 < inks.length; i++) height += wordRowPitch(rows.pitch, inks[i]!, inks[i + 1]!)
  return height
}

type RowInk = { above: number; below: number; baseline: number }

/** A matrix's rows (`mtr`), each row's ink about its baseline (null for a row without ink) and Word's row pitch (px). */
function matrixRows(table: Element): { rows: HTMLElement[]; inks: (RowInk | null)[]; pitch: number } | null {
  const rows = [...table.children].filter(row => row.tagName.toLowerCase() === 'mtr') as HTMLElement[]
  const pitchEm = parseFloat((table as HTMLElement).dataset.pitchEm ?? rows[0]?.style.height ?? '')
  const size = parseFloat(getComputedStyle(table).fontSize)
  if (!(pitchEm > 0) || !(size > 0)) return null
  const inks = rows.map(row => {
    let top = Infinity, bottom = -Infinity, baseline = NaN
    for (const token of row.querySelectorAll<HTMLElement>(INK_TOKENS)) {
      // a nested matrix's tokens are its own rows' (measured before its rows move): the row is left as it is
      if (token.closest('mtable') !== table) return null
      const ink = tokenInk(token)
      if (!ink) continue
      if (Number.isNaN(baseline)) baseline = ink.baseline
      top = Math.min(top, ink.top); bottom = Math.max(bottom, ink.bottom)
    }
    return Number.isNaN(baseline) ? null : { above: baseline - top, below: bottom - baseline, baseline }
  })
  return { rows, inks, pitch: pitchEm * size }
}

/** Word's distance between two rows' baselines: one math line, more when their ink would meet. */
function wordRowPitch(pitch: number, upper: RowInk, lower: RowInk): number {
  return Math.max(pitch, upper.below + lower.above)
}

/**
 * The space that puts each matrix row Word's pitch below the row above it. Word sets rows one math line apart (1.18 em,
 * test-data/word-math/brackets-*-v1.pdf); a minimum row height of that line (the library's) let Chromium place rows ~1.32 em
 * apart (the cell's baseline sits low in the taller box), so the rows are laid out without it and spaced from their baselines.
 * Padding above a row's cells moves it and the rows below it down by that much.
 */
function matrixRowShifts(table: Element): { row: HTMLElement; shift: number }[] {
  const read = matrixRows(table)
  if (!read) return []
  const shifts: { row: HTMLElement; shift: number }[] = []
  for (let i = 0; i + 1 < read.rows.length; i++) {
    const upper = read.inks[i], lower = read.inks[i + 1]
    if (!upper || !lower) continue
    const shift = wordRowPitch(read.pitch, upper, lower) - (lower.baseline - upper.baseline)
    if (Math.abs(shift) >= 0.25) shifts.push({ row: read.rows[i + 1]!, shift })
  }
  return shifts
}

/** The space a row was given above its cells (px; 0 without). */
function rowShift(row: HTMLElement): number {
  return parseFloat((row.firstElementChild as HTMLElement | null)?.style.paddingTop ?? '') || 0
}

/** The space above a row's cells (px): padding moves the row down (none for a shift at or below 0). */
function setRowShift(row: HTMLElement, shift: number | null) {
  for (const cell of row.children) (cell as HTMLElement).style.paddingTop = shift === null || shift <= 0 ? '' : `${shift}px`
}

type Variants = { heights: number[]; widths: number[]; assembly: number }

function parseVariants(value: string): Variants {
  const [list = '', assembly = '0'] = value.split('|')
  const pairs = list.trim().split(/\s+/).map(pair => pair.split(':').map(Number)).filter(p => p.length === 2 && p.every(Number.isFinite))
  return { heights: pairs.map(p => p[0]!), widths: pairs.map(p => p[1]!), assembly: Number(assembly) }
}

/** Word's pick for a stretched height (em): the smallest variant at least that tall, else the assembly's width. */
export function wordVariantWidth(variants: Variants, heightEm: number): number {
  for (let i = 0; i < variants.heights.length; i++) if (variants.heights[i]! >= heightEm) return variants.widths[i]!
  return variants.assembly > 0 ? variants.assembly : variants.widths[variants.widths.length - 1]!
}

/** The index of the first piece of each line, read from the pieces' boxes (a piece wholly below the line starts the next). */
function lineStarts(boxes: DOMRect[]): number[] {
  const starts: number[] = []
  let bottom = -Infinity
  boxes.forEach((box, i) => {
    if (i > 0 && box.top < bottom - 1) { bottom = Math.max(bottom, box.bottom); return }
    starts.push(i)
    bottom = box.bottom
  })
  return starts
}

function setRepeat(repeat: HTMLElement, piece: HTMLElement | undefined, previous: HTMLElement | undefined, show: boolean) {
  repeat.style.display = show ? '' : 'none'
  const end = repeat.getAttribute('data-line-end')
  const ending = end ? previous?.querySelector<HTMLElement>(':scope > mrow > mo:last-of-type') : null
  if (!ending || !end || !piece) return
  if (show && !ending.hasAttribute('data-original-operator')) { ending.setAttribute('data-original-operator', ending.textContent ?? ''); ending.textContent = end }
  else if (!show && ending.hasAttribute('data-original-operator')) { ending.textContent = ending.getAttribute('data-original-operator'); ending.removeAttribute('data-original-operator') }
}

/**
 * Lays out all the equations under `root` with batched measurement: each pass writes, then reads every block and operator at
 * once, never a read per operator. Pass 1 reads the stretched operators and the lines without repeated operators; pass 2
 * reads the lines with the copies shown where pass 1 found line starts (a shown copy widens its line and can move a later
 * break); a block whose starts moved gets one more pass. At most three reads per render, whatever the number of operators,
 * and two before them when the render holds a matrix (its rows are put Word's pitch apart first: matrixRowShifts, then
 * corrected by what Chromium added to the padding).
 */
export function layoutMath(root: Element) {
  const stretched = [...root.querySelectorAll<HTMLElement>(STRETCH)]
  const blocks = [...root.querySelectorAll<HTMLElement>(BLOCKS)]
  const pieceLists = blocks.map(block => [...block.querySelectorAll<HTMLElement>(':scope > math')])
  const repeatLists = pieceLists.map(pieces => pieces.map(piece => piece.querySelector<HTMLElement>(':scope > mrow > mo.omath-repeat')))
  const matrices = [...root.querySelectorAll<HTMLElement>(MATRICES)]
  // write: back to the library's output, copies hidden
  for (const operator of stretched) operator.style.marginInline = ''
  for (const matrix of matrices) {
    // the library's row height (Word's pitch, a minimum for hosts without this layout) is kept on the table: a minimum would
    // stop a row from closing up to the pitch
    if (matrix.dataset.pitchEm === undefined) matrix.dataset.pitchEm = String(parseFloat((matrix.firstElementChild as HTMLElement | null)?.style.height ?? '') || '')
    for (const row of matrix.children) { (row as HTMLElement).style.height = ''; setRowShift(row as HTMLElement, null) }
  }
  // a matrix's rows first (one more read when the render holds one): a stretched delimiter around it takes its width from the
  // rows' final height
  if (matrices.length > 0) {
    for (const { row, shift } of matrices.flatMap(matrixRowShifts)) setRowShift(row, shift)
    // a padded row lands ~0.85 px further down than its padding (Chromium's cell baseline alignment: 1.226 em apart for
    // Word's 1.18 in Cambria Math): one more read takes what is left off each padding
    for (const { row, shift } of matrices.flatMap(matrixRowShifts)) setRowShift(row, rowShift(row) + shift)
  }
  blocks.forEach((block, b) => {
    // the library's own alignment (m:jc) is in the block's style: kept once, restored on every run
    if (!block.hasAttribute('data-align')) block.setAttribute('data-align', block.style.textAlign)
    block.style.textAlign = block.getAttribute('data-align') ?? ''
    pieceLists[b]!.forEach((piece, i) => { piece.style.marginInlineEnd = ''; const repeat = repeatLists[b]![i]; if (repeat) setRepeat(repeat, piece, pieceLists[b]![i - 1], false) })
  })
  // read 1: stretched operators and the lines
  const stretchReads = stretched.map(operator => {
    const box = operator.getBoundingClientRect()
    const size = parseFloat(getComputedStyle(operator).fontSize) || 16
    const content = contentOf(operator)
    const extent = content ? inkExtent(content) : null
    // the stretched operator is centred on the math axis: the axis is its middle
    const axis = box.top + box.height / 2
    const matrix = content ? matrixInkHeight(content) : null
    const distance = matrix !== null ? matrix / 2 : extent ? Math.max(axis - extent.top, extent.bottom - axis) : box.height / 2
    const needed = Math.max(2 * distance * DELIMITER_FACTOR, 2 * distance - DELIMITER_SHORTFALL_EM * size)
    return { operator, width: box.width, height: needed, size }
  })
  let starts = pieceLists.map(pieces => lineStarts(pieces.map(piece => piece.getBoundingClientRect())))
  // write: stretched widths, copies at the line starts
  for (const { operator, width, height, size } of stretchReads) {
    if (!(height > 0) || !(size > 0)) continue
    const wanted = wordVariantWidth(parseVariants(operator.getAttribute('data-variants') ?? ''), height / size) * size
    if (wanted > 0 && Math.abs(wanted - width) >= 0.25) operator.style.marginInline = `${(wanted - width) / 2}px`
  }
  const repeating = blocks.map(block => block.hasAttribute('data-math-break'))
  const showAt = (b: number, lines: number[]) => {
    const set = new Set(lines.slice(1))
    repeatLists[b]!.forEach((repeat, i) => { if (repeat) setRepeat(repeat, pieceLists[b]![i], pieceLists[b]![i - 1], set.has(i) && pieceLists[b]![i]!.previousElementSibling?.tagName.toLowerCase() !== 'br') })
  }
  for (let pass = 0; pass < 2; pass++) {
    const pending = blocks.map((_, b) => repeating[b]!)
    if (!pending.some(Boolean)) break
    pending.forEach((on, b) => { if (on) showAt(b, starts[b]!) })
    // read: the lines with the copies shown; settled blocks are done
    const next = pieceLists.map((pieces, b) => pending[b] ? lineStarts(pieces.map(piece => piece.getBoundingClientRect())) : starts[b]!)
    const moved = next.map((lines, b) => lines.join(',') !== starts[b]!.join(','))
    starts = next
    if (!moved.some(Boolean)) break
    moved.forEach((m, b) => { if (!m) repeating[b] = false })
  }
  // best effort when the lines did not settle in three reads (a copy can then sit one piece off a line start)
  repeating.forEach((_, b) => { if (blocks[b]!.hasAttribute('data-math-break')) showAt(b, starts[b]!) })
  // wrapRight: read the first lines once, then write
  const rights = blocks.map((block, b) => {
    if (!block.hasAttribute('data-math-wrap-right') || starts[b]!.length < 2) return null
    const style = getComputedStyle(block)
    const pieces = pieceLists[b]!
    const lastOfFirst = starts[b]![1]! - 1
    const used = pieces[lastOfFirst]!.getBoundingClientRect().right - pieces[0]!.getBoundingClientRect().left
    const room = block.clientWidth - parseFloat(style.paddingLeft || '0') - parseFloat(style.paddingRight || '0') - parseFloat(style.textIndent || '0')
    return { lastOfFirst, free: Math.max(0, room - used), align: style.textAlign }
  })
  rights.forEach((right, b) => {
    if (!right) return
    const keep = right.align === 'center' ? right.free / 2 : right.align === 'right' || right.align === 'end' ? 0 : right.free
    blocks[b]!.style.textAlign = 'right'
    if (keep > 0.5) pieceLists[b]![right.lastOfFirst]!.style.marginInlineEnd = `${keep}px`
  })
}

const observers = new WeakMap<HTMLElement, ResizeObserver>()

/** `v-fast-doc-view-math` on the element that holds block HTML: finishes its equations after each render and on resize. */
export const vFastDocViewMath = {
  mounted(element: HTMLElement) { schedule(element) },
  updated(element: HTMLElement) { schedule(element) },
  unmounted(element: HTMLElement) { observers.get(element)?.disconnect(); observers.delete(element) },
}

function schedule(element: HTMLElement) {
  if (!element.querySelector(`${STRETCH}, ${BLOCKS}, ${MATRICES}`)) return
  const run = () => {
    if (!element.isConnected) return
    const started = performance.now()
    layoutMath(element)
    if (import.meta.env.DEV) (window as unknown as { __fastDocViewMathMs?: number[] }).__fastDocViewMathMs = [...((window as unknown as { __fastDocViewMathMs?: number[] }).__fastDocViewMathMs ?? []), performance.now() - started].slice(-50)
  }
  if (!observers.has(element) && typeof ResizeObserver !== 'undefined') {
    let width = element.clientWidth
    const observer = new (element.ownerDocument.defaultView ?? window).ResizeObserver(() => { if (element.clientWidth !== width) { width = element.clientWidth; run() } })
    observer.observe(element)
    observers.set(element, observer)
  }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}
