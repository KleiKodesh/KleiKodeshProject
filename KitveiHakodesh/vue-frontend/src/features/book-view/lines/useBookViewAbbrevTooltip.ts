/**
 * Abbreviation tooltip for the book view.
 *
 * When the user selects a single complete word that looks like a Hebrew
 * abbreviation — gershayim in the middle (רשב"א) or a geresh at the end (מת') —
 * it is looked up in the dictionary DB (dictionary senses only, never the
 * seforim DB) and the expansions are shown in a compact tooltip anchored to
 * the selection. Exact headword match first, then a %term% LIKE fallback.
 * No match → no tooltip.
 *
 * Trigger rules:
 *   - Left-button mouseup inside the scroller with a non-collapsed selection.
 *   - The selection must cover exactly one word: surrounding non-word chars
 *     are stripped, but the remaining core must contain no whitespace and the
 *     characters adjacent to the selection boundaries must not be Hebrew
 *     letters (partial-word selections are rejected).
 *   - ״/curly quotes are normalized to ASCII "/' — the dictionary stores
 *     abbreviation headwords with ASCII quote characters only.
 *
 * Dismissal: selection collapses or leaves, or the scroller scrolls
 * (the anchor rect goes stale).
 */
import { ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { dictAbbrevSenses } from '@/webview-host/dictionaryDb'

export interface AbbrevSense {
  headword: string
  text: string
}

export interface AbbrevTooltipData {
  /** Unique per lookup — used as component key so a new selection remounts/re-measures. */
  id: number
  term: string
  senses: AbbrevSense[]
  anchorRect: DOMRect
}

// U+0591–U+05C7 — nikud + cantillation marks
const HEBREW_MARKS = /[\u0591-\u05C7]/g
const WORD_CHAR = /[א-ת\u0591-\u05C7]/
// Gershayim in the middle (רשב"א) or geresh at the end (מת').
const ABBREV_PATTERN = /^(?:[א-ת]+"[א-ת]+|[א-ת]+')$/

/**
 * Normalizes the raw selection string and returns the abbreviation term,
 * or null when the selection is not a single abbreviation-shaped word.
 */
export function extractAbbrevTerm(raw: string): string | null {
  let text = raw
    .replace(HEBREW_MARKS, '')
    .replace(/[״”“]/g, '"')
    .replace(/[׳’‘]/g, "'")
    .trim()
  if (!text || /\s/.test(text)) return null
  // Strip surrounding non-word chars; trailing '/" are kept so the pattern
  // itself decides whether they are part of the abbreviation.
  text = text.replace(/^[^א-ת]+/, '').replace(/[^א-ת'"]+$/, '')
  return ABBREV_PATTERN.test(text) ? text : null
}

function isBoundaryChar(ch: string | undefined): boolean {
  return ch === undefined || !WORD_CHAR.test(ch)
}

/** The characters adjacent to the selection must not be word characters. */
function isFullWordSelection(range: Range): boolean {
  const { startContainer, startOffset, endContainer, endOffset } = range
  let before: string | undefined
  if (startContainer.nodeType === Node.TEXT_NODE && startOffset > 0) {
    before = startContainer.textContent?.[startOffset - 1]
  }
  let after: string | undefined
  if (endContainer.nodeType === Node.TEXT_NODE) {
    after = endContainer.textContent?.[endOffset]
  }
  return isBoundaryChar(before) && isBoundaryChar(after)
}

export function useBookViewAbbrevTooltip(scrollerEl: Ref<HTMLElement | null>) {
  const abbrevTooltip = ref<AbbrevTooltipData | null>(null)
  let lookupToken = 0

  function closeAbbrevTooltip() {
    lookupToken++
    abbrevTooltip.value = null
  }

  async function onSelectionSettled() {
    const sel = window.getSelection()
    if (!sel || sel.isCollapsed || sel.rangeCount === 0) { closeAbbrevTooltip(); return }
    const range = sel.getRangeAt(0)
    const root = scrollerEl.value
    if (!root || !root.contains(range.commonAncestorContainer)) { closeAbbrevTooltip(); return }

    const term = extractAbbrevTerm(sel.toString())
    if (!term || !isFullWordSelection(range)) { closeAbbrevTooltip(); return }

    const token = ++lookupToken
    let rows
    try {
      rows = await dictAbbrevSenses(term)
    } catch {
      return
    }
    if (token !== lookupToken) return

    const seen = new Set<string>()
    const senses: AbbrevSense[] = []
    for (const row of rows) {
      if (seen.has(row.text)) continue
      seen.add(row.text)
      senses.push({ headword: row.headword, text: row.text })
    }
    if (!senses.length) { abbrevTooltip.value = null; return }

    abbrevTooltip.value = { id: token, term, senses, anchorRect: range.getBoundingClientRect() }
  }

  function onMouseUp(event: MouseEvent) {
    if (event.button !== 0) return
    // Defer — the selection is not final until after mouseup's default action.
    setTimeout(onSelectionSettled, 0)
  }

  useEventListener(scrollerEl, 'mouseup', onMouseUp)
  useEventListener(scrollerEl, 'scroll', closeAbbrevTooltip, { passive: true })
  useEventListener(document, 'selectionchange', () => {
    const sel = window.getSelection()
    if (!sel || sel.isCollapsed) closeAbbrevTooltip()
  })

  return { abbrevTooltip, closeAbbrevTooltip }
}
