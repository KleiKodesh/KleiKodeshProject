/**
 * Equations finished with the browser's layout - what the library's HTML writer cannot know (FastDocView
 * Html/WordOfficeMathHtml.cs). Measurement is batched for all the blocks a render holds: writes, then one read of every
 * block and operator, never a read per operator (layoutMath: at most three reads per render).
 *
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

/** The content's ink extent (px, top / bottom) from its tokens' boxes and text metrics. */
function inkExtent(content: Element): { top: number; bottom: number } | null {
  let top = Infinity, bottom = -Infinity
  for (const token of content.querySelectorAll<HTMLElement>('mi, mn, mo:not(.omath-stretch), mtext')) {
    const text = token.textContent ?? ''
    if (!text.trim() || token.closest('mphantom')) continue
    const box = token.getBoundingClientRect()
    if (!(box.height > 0)) continue
    const style = getComputedStyle(token)
    const ink = inkOf(text, `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`)
    if (!ink) continue
    // a token box is either the font's line box or the ink box: its baseline sits accordingly
    const lineBox = ink.fontAscent + ink.fontDescent
    if (!(lineBox > 0) || !Number.isFinite(ink.ascent) || !Number.isFinite(ink.descent)) continue
    const baseline = Math.abs(box.height - lineBox) < Math.abs(box.height - (ink.ascent + ink.descent)) ? box.top + ink.fontAscent * box.height / lineBox : box.top + ink.ascent
    top = Math.min(top, baseline - ink.ascent)
    bottom = Math.max(bottom, baseline + ink.descent)
  }
  return top < bottom ? { top, bottom } : null
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
 * break); a block whose starts moved gets one more pass. At most three reads per render, whatever the number of operators.
 */
export function layoutMath(root: Element) {
  const stretched = [...root.querySelectorAll<HTMLElement>(STRETCH)]
  const blocks = [...root.querySelectorAll<HTMLElement>(BLOCKS)]
  const pieceLists = blocks.map(block => [...block.querySelectorAll<HTMLElement>(':scope > math')])
  const repeatLists = pieceLists.map(pieces => pieces.map(piece => piece.querySelector<HTMLElement>(':scope > mrow > mo.omath-repeat')))
  // write: back to the library's output, copies hidden
  for (const operator of stretched) operator.style.marginInline = ''
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
    const distance = extent ? Math.max(axis - extent.top, extent.bottom - axis) : box.height / 2
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
  if (!element.querySelector(`${STRETCH}, ${BLOCKS}`)) return
  const run = () => {
    if (!element.isConnected) return
    const started = performance.now()
    layoutMath(element)
    if (import.meta.env.DEV) (window as unknown as { __fastDocViewMathMs?: number[] }).__fastDocViewMathMs = [...((window as unknown as { __fastDocViewMathMs?: number[] }).__fastDocViewMathMs ?? []), performance.now() - started].slice(-50)
  }
  if (!observers.has(element) && typeof ResizeObserver !== 'undefined') {
    let width = element.clientWidth
    const observer = new ResizeObserver(() => { if (element.clientWidth !== width) { width = element.clientWidth; run() } })
    observer.observe(element)
    observers.set(element, observer)
  }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}
