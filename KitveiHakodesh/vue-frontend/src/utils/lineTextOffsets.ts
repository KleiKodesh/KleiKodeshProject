/**
 * Conversions between a DOM position inside a rendered `.line` element and a
 * character offset into that line's DIACRITIC-STRIPPED text.
 *
 * The stripped-text offset is the coordinate space the whole annotation system
 * speaks: highlights and notes are persisted as {startOffset, endOffset} counted
 * over the line's text with niqqud and te'amim (U+0591-U+05C7) removed, and the
 * renderer splices its <mark> and <sup> elements back in at those same offsets.
 *
 * Both directions skip nothing except the diacritics themselves — note markers and
 * word-link markers render as EMPTY <sup> elements whose glyph comes from CSS
 * ::before, so they contribute no text nodes and the two walks cannot disagree
 * about where a character sits.
 *
 * The pair is exact inverses over every position a selection can hold, which is
 * what lets a selection be re-created after the line's markup is replaced (see
 * useBookViewSelectionPinnedRows for why that is needed).
 */

/** Matches the Hebrew diacritics excluded from the offset coordinate space. */
const DIACRITICS_PATTERN = /[֑-ׇ]/g

/** The line's text as the annotation offsets count it. */
export function strippedLineText(lineElement: HTMLElement): string {
  return (lineElement.textContent ?? '').replace(DIACRITICS_PATTERN, '')
}

/**
 * DOM position → stripped-text offset.
 *
 * `node` is a boundary container of a Range and `offsetInNode` its offset. When the
 * node is not a text node inside this line (a Range boundary can land on an element,
 * and a collapsed-into-parent boundary lands on the line itself) the walk runs off
 * the end and returns the full stripped length, which callers clamp.
 */
export function strippedOffsetFromDomPosition(
  lineElement: HTMLElement,
  node: Node,
  offsetInNode: number,
): number {
  const walker = document.createTreeWalker(lineElement, NodeFilter.SHOW_TEXT)
  let strippedCount = 0
  let currentNode: Text | null
  while ((currentNode = walker.nextNode() as Text | null)) {
    if (currentNode === node) {
      const slice = currentNode.textContent?.slice(0, offsetInNode) ?? ''
      return strippedCount + slice.replace(DIACRITICS_PATTERN, '').length
    }
    strippedCount += (currentNode.textContent ?? '').replace(DIACRITICS_PATTERN, '').length
  }
  return strippedCount
}

/**
 * Stripped-text offset → DOM position, the inverse of the walk above.
 *
 * Returns the text node containing that character and the offset within it, or null
 * when the line has no text at all. An offset past the end of the line clamps to the
 * end of the last text node, so a stale offset degrades to a shorter selection
 * rather than throwing.
 *
 * A position that falls exactly on a boundary between two text nodes resolves to the
 * END of the earlier node rather than the start of the later one. Both are the same
 * caret position to the browser, so which one is returned only matters for keeping
 * this function deterministic.
 */
export function domPositionFromStrippedOffset(
  lineElement: HTMLElement,
  strippedOffset: number,
): { node: Text; offset: number } | null {
  const walker = document.createTreeWalker(lineElement, NodeFilter.SHOW_TEXT)
  let remaining = strippedOffset
  let lastTextNode: Text | null = null
  let currentNode: Text | null

  while ((currentNode = walker.nextNode() as Text | null)) {
    const text = currentNode.textContent ?? ''
    const strippedLength = text.replace(DIACRITICS_PATTERN, '').length
    lastTextNode = currentNode

    // `<=` rather than `<`: an offset landing exactly at this node's end resolves
    // here, at the node's end, instead of falling through to the next node's start.
    if (remaining <= strippedLength) {
      return { node: currentNode, offset: rawOffsetFromStrippedOffset(text, remaining) }
    }
    remaining -= strippedLength
  }

  if (!lastTextNode) return null
  return { node: lastTextNode, offset: (lastTextNode.textContent ?? '').length }
}

/**
 * Walks one text node's raw characters until `strippedOffset` non-diacritic
 * characters have passed, and returns the raw index reached.
 *
 * Diacritics immediately FOLLOWING the target character are consumed too, so a
 * selection boundary lands after a letter's vowel points rather than between the
 * letter and its own marks — splitting those apart renders as a stray floating vowel.
 */
function rawOffsetFromStrippedOffset(text: string, strippedOffset: number): number {
  let remaining = strippedOffset
  let rawIndex = 0
  while (rawIndex < text.length && remaining > 0) {
    if (!isDiacritic(text[rawIndex]!)) remaining--
    rawIndex++
  }
  while (rawIndex < text.length && isDiacritic(text[rawIndex]!)) rawIndex++
  return rawIndex
}

function isDiacritic(character: string): boolean {
  const code = character.charCodeAt(0)
  return code >= 0x0591 && code <= 0x05c7
}
