/**
 * Display equations finished with the browser's line breaks - what the library's HTML writer cannot know. The library cuts a
 * long display equation into consecutive math elements at its operators (FastDocView Html/WordOfficeMathHtml.cs
 * AppendBreakable) and marks the equation's block (`.omath-para`) with the document's settings that depend on where a line
 * actually breaks (ECMA-376 Part 1 22.1.2):
 *
 * - `data-math-break="repeat"` (m:brkBin repeat, 22.1.2.15): the operator ends a broken line and starts the next one again. Each
 *   piece after a cut starts with a hidden copy of the operator (`mo.omath-repeat`); it is shown when its piece starts a line,
 *   and a minus whose copy carries `data-line-end` (m:brkBinSub "+-", 22.1.2.16) ends its line as that sign.
 * - `data-math-wrap-right` (m:wrapRight, 22.1.2.121): every continuation line is moved to the right edge.
 *
 * Runs on block HTML already in the page (the directive below) and again when the block's width changes.
 */

type Piece = { element: HTMLElement; top: number; bottom: number }

/** The pieces by line: a piece that overlaps the current line vertically is on it (pieces share a baseline, a fraction
 *  reaches higher than the letters beside it), one wholly below starts the next line. */
function linesOf(block: HTMLElement): Piece[][] {
  const lines: Piece[][] = []
  let bottom = -Infinity
  for (const element of block.querySelectorAll<HTMLElement>(':scope > math')) {
    const box = element.getBoundingClientRect()
    const line = lines[lines.length - 1]
    if (line && box.top < bottom - 1) { line.push({ element, top: box.top, bottom: box.bottom }); bottom = Math.max(bottom, box.bottom) }
    else { lines.push([{ element, top: box.top, bottom: box.bottom }]); bottom = box.bottom }
  }
  return lines
}

function resetBlock(block: HTMLElement) {
  for (const repeat of block.querySelectorAll<HTMLElement>('mo.omath-repeat')) repeat.style.display = 'none'
  for (const ending of block.querySelectorAll<HTMLElement>('[data-original-operator]')) {
    ending.textContent = ending.getAttribute('data-original-operator')
    ending.removeAttribute('data-original-operator')
  }
  for (const piece of block.querySelectorAll<HTMLElement>(':scope > math')) piece.style.marginInlineEnd = ''
  block.style.textAlign = ''
}

/** The last operator of the piece before `piece` (the one a repeated break ends its line with). */
function lineEndOperator(piece: HTMLElement): HTMLElement | null {
  let previous = piece.previousElementSibling
  while (previous && previous.tagName.toLowerCase() !== 'math') previous = previous.previousElementSibling
  const operators = previous?.querySelectorAll<HTMLElement>(':scope > mrow > mo')
  return operators && operators.length > 0 ? operators[operators.length - 1]! : null
}

/** Shows or hides one piece's repeated operator (and, for brkBinSub "+-", the sign its previous line ends with). */
function setRepeat(repeat: HTMLElement, show: boolean) {
  repeat.style.display = show ? '' : 'none'
  const end = repeat.getAttribute('data-line-end')
  const ending = end ? lineEndOperator(repeat.closest('math') as HTMLElement) : null
  if (!ending || !end) return
  if (show && !ending.hasAttribute('data-original-operator')) { ending.setAttribute('data-original-operator', ending.textContent ?? ''); ending.textContent = end }
  else if (!show && ending.hasAttribute('data-original-operator')) { ending.textContent = ending.getAttribute('data-original-operator'); ending.removeAttribute('data-original-operator') }
}

/**
 * Shows the repeated operator of each piece that starts a line, and only those, from the top down: a shown operator
 * widens its line and can move the later breaks, never the earlier ones, so each piece is decided with the lines above it
 * final (the lines are measured again after every change).
 */
function showRepeats(block: HTMLElement) {
  for (const repeat of block.querySelectorAll<HTMLElement>(':scope > math > mrow > mo.omath-repeat')) {
    const piece = repeat.closest('math') as HTMLElement
    const starts = linesOf(block).slice(1).some(line => line[0]!.element === piece)
    if ((repeat.style.display !== 'none') !== starts) setRepeat(repeat, starts)
  }
}

export function layoutMathBlock(block: HTMLElement) {
  resetBlock(block)
  // the two depend on each other (a shown operator moves breaks, the right alignment can too): until the lines settle
  let signature = ''
  for (let pass = 0; pass < 4; pass++) {
    if (block.hasAttribute('data-math-break')) showRepeats(block)
    if (block.hasAttribute('data-math-wrap-right')) alignRight(block)
    const now = linesOf(block).map(line => line.length).join(',')
    if (now === signature) break
    signature = now
  }
}

/** m:wrapRight: the continuation lines at the right edge, the first line where its own alignment puts it. */
function alignRight(block: HTMLElement) {
  for (const piece of block.querySelectorAll<HTMLElement>(':scope > math')) piece.style.marginInlineEnd = ''
  block.style.textAlign = ''
  {
    const lines = linesOf(block)
    if (lines.length < 2) return
    // continuation lines at the right edge: the block right-aligned, and the first line kept where its own alignment puts it
    // by a margin after its last piece (it never overflows: at most the free room on that line)
    const style = getComputedStyle(block)
    const align = style.textAlign
    const box = block.getBoundingClientRect()
    const width = box.width - parseFloat(style.paddingLeft || '0') - parseFloat(style.paddingRight || '0')
    const first = lines[0]!
    const used = first[first.length - 1]!.element.getBoundingClientRect().right - first[0]!.element.getBoundingClientRect().left
    const free = Math.max(0, width - used - parseFloat(style.textIndent || '0'))
    block.style.textAlign = 'right'
    const keep = align === 'center' ? free / 2 : align === 'right' || align === 'end' ? 0 : free
    if (keep > 0.5) first[first.length - 1]!.element.style.marginInlineEnd = `${keep}px`
  }
}

export function layoutMath(root: Element) {
  for (const block of root.querySelectorAll<HTMLElement>('.omath-para[data-math-break], .omath-para[data-math-wrap-right]')) layoutMathBlock(block)
}

const observers = new WeakMap<HTMLElement, ResizeObserver>()

/** `v-fast-doc-view-math` on the element that holds block HTML: finishes its display equations after each render and on resize. */
export const vFastDocViewMath = {
  mounted(element: HTMLElement) { schedule(element) },
  updated(element: HTMLElement) { schedule(element) },
  unmounted(element: HTMLElement) { observers.get(element)?.disconnect(); observers.delete(element) },
}

function schedule(element: HTMLElement) {
  if (!element.querySelector('.omath-para[data-math-break], .omath-para[data-math-wrap-right]')) return
  const run = () => { if (element.isConnected) layoutMath(element) }
  if (!observers.has(element) && typeof ResizeObserver !== 'undefined') {
    let width = element.clientWidth
    const observer = new ResizeObserver(() => { if (element.clientWidth !== width) { width = element.clientWidth; run() } })
    observer.observe(element)
    observers.set(element, observer)
  }
  if (document.fonts?.ready) document.fonts.ready.then(run)
  else run()
}
