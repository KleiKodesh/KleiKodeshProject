/**
 * Equations finished with the browser's layout - what the library's HTML writer cannot know (FastDocView
 * Html/WordOfficeMathHtml.cs). Measurement is batched for all the blocks a render holds: writes, then one read of every
 * block and operator, never a read per operator (layoutMath: at most three reads per render).
 *
 * - Stretched operators (`mo.omath-stretch[data-variants]`): Word draws a grown delimiter or n-ary operator with the
 *   font's OpenType MATH size variants, else its assembly; the operator carries them ("height:width ... |assembly width", em).
 *   The browser stretched the operator to a height; Word picks the smallest variant at least 1.2 times that tall for a
 *   delimiter, 0.7 times for an n-ary operator (else the assembly) - factors fitted to Word 16's PDFs of 3-, 5- and 10-row
 *   brackets and growing integrals in Cambria Math and DejaVu Math TeX Gyre (test-data/word-math/brackets-*-v1.pdf) -
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

const DELIMITER_FACTOR = 1.2, NARY_FACTOR = 0.7

type Variants = { heights: number[]; widths: number[]; assembly: number }

function parseVariants(value: string): Variants {
  const [list = '', assembly = '0'] = value.split('|')
  const pairs = list.trim().split(/\s+/).map(pair => pair.split(':').map(Number))
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
    block.style.textAlign = ''
    pieceLists[b]!.forEach((piece, i) => { piece.style.marginInlineEnd = ''; const repeat = repeatLists[b]![i]; if (repeat) setRepeat(repeat, piece, pieceLists[b]![i - 1], false) })
  })
  // read 1: stretched operators and the lines
  const stretchReads = stretched.map(operator => {
    const box = operator.getBoundingClientRect()
    const size = parseFloat(getComputedStyle(operator).fontSize) || 16
    return { operator, width: box.width, height: box.height, size, factor: operator.getAttribute('data-stretch') === 'nary' ? NARY_FACTOR : DELIMITER_FACTOR }
  })
  let starts = pieceLists.map(pieces => lineStarts(pieces.map(piece => piece.getBoundingClientRect())))
  // write: stretched widths, copies at the line starts
  for (const { operator, width, height, size, factor } of stretchReads) {
    if (!(height > 0) || !(size > 0)) continue
    const wanted = wordVariantWidth(parseVariants(operator.getAttribute('data-variants') ?? ''), factor * height / size) * size
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
