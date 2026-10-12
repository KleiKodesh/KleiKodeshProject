/**
 * Word-level link anchor splicing (SeforimLibrary schema v2 `link_anchor` rows).
 *
 * Range anchors (charEnd != null) wrap the anchored citation text in a clickable
 * `<span class="word-link" data-wl="…">`; point anchors insert an empty
 * `<sup class="word-link-marker" data-wl="…" data-wl-label="…"></sup>` (plus a
 * `data-wl-c` color/shape bucket for undecorated labels) whose label
 * renders via CSS `content: attr(data-wl-label)` — deliberately NO text content, so
 * the spliced markup adds zero visible characters and none of the downstream
 * offset-based walkers (user highlights, note markers, search marks — all of which
 * skip tags) ever drift. For the same reason this must run FIRST in the render
 * pipeline, on the RAW db content, before the diacritics filter and the divine-name
 * censor (both of which change visible-char counts).
 *
 * ⚠ Offset convention — NOT the same as stripHtmlForSearch:
 * charStart/charEnd are produced by upstream's countVisibleChars (HtmlCharCounter.kt):
 *   - HTML tags count 0 chars
 *   - each entity counts 1 char; `&` starts an entity iff a `;` occurs within the
 *     next 9 chars (bail window i+10, NO whitespace check — mirror it exactly)
 *   - every other char counts 1 — INCLUDING nikud/te'amim (the app's own walkers
 *     drop diacritics; verified upstream == app + diacritics on 6k real lines,
 *     and 17/17 real v17 anchors extract clean citations under this walk, 2026-07-27)
 *
 * `data-wl` carries "targetBookId:targetLineIndex:targetLineId" for the click/hover
 * handlers (see useWordLinkTooltip.ts).
 */
import type { WordLinkAnchor, WordLinkTargetRow } from '@/webview-host/queries.types'
export interface WordLinkTarget {
  bookId: number
  lineIndex: number
  lineId: number
}

/** Parse a `data-wl` attribute value back into its navigation target. */
export function parseWordLinkData(raw: string | null): WordLinkTarget | null {
  if (!raw) return null
  const parts = raw.split(':')
  if (parts.length !== 3) return null
  const bookId = Number(parts[0])
  const lineIndex = Number(parts[1])
  const lineId = Number(parts[2])
  if (!Number.isFinite(bookId) || !Number.isFinite(lineIndex) || !Number.isFinite(lineId)) return null
  return { bookId, lineIndex, lineId }
}

/**
 * Citation links some databases (Zayit) carry INSIDE the line text itself:
 * `<a class="ref" href="seforim://line/<lineId>">`. They need no splicing — only the
 * same hover/click handling as `[data-wl]`. The href names the target line alone, so
 * its book and index are looked up on use.
 */
export const INLINE_REF_SELECTOR = 'a.ref[href^="seforim://line/"]'

/** Every element the hover/click handlers treat as a link. */
export const WORD_LINK_SELECTOR = `[data-wl], ${INLINE_REF_SELECTOR}`

/** The target line id of an inline `seforim://line/<id>` ref, or null. */
export function parseInlineRefLineId(href: string | null): number | null {
  const match = href?.match(/^seforim:\/\/line\/(\d+)$/)
  return match ? Number(match[1]) : null
}

function escapeAttr(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

const wlData = (a: WordLinkAnchor) => `${a.targetBookId}:${a.targetLineIndex}:${a.targetLineId}`

const openTag = (a: WordLinkAnchor) => `<span class="word-link" data-wl="${wlData(a)}">`

/** Color buckets for point markers — must match the data-wl-c palette in main.css. */
const MARKER_COLOR_BUCKETS = 8

/** Fallback treatment for one commentary book: palette slot + optional enclosure glyphs + optional underline. */
export interface WordLinkTreatment {
  bucket: number
  open?: string
  close?: string
  underline?: boolean
}

/**
 * Enclosure candidates for the fallback slots, in preference order. Chosen at runtime
 * per book: a pair is skipped when either glyph appears in any of the book's own labels,
 * so an app-assigned wrapper can never imitate a sign the source edition uses — whatever
 * signs a future DB introduces.
 */
const ENCLOSURE_CANDIDATES: ReadonlyArray<readonly [string, string]> = [
  ['(', ')'],
  ['[', ']'],
  ['{', '}'],
  ['⟨', '⟩'],
  ['⟪', '⟫'],
  ['⦅', '⦆'],
]

/**
 * Assign the fallback treatments for one source book's word-link targets.
 *
 * Ranking: distinct commentary books that have at least one undecorated label (only those
 * ever use the fallback). High-priority reference types (Ein Mishpat עין משפט and 
 * Mesorah Hashas מסורת הש״ס) get first priority with the simplest treatment (plain, no decoration). 
 * After high-priority refs, other books are sorted by tree order (catalog hierarchy) — then by
 * ascending book id as tiebreaker. Treatments cycle: plain → underlined → enclosure 1 → enclosure 2 → etc.
 * Each rank gets a unique treatment, so commentary books are always visually distinct.
 * Custom delimiters (labels with their own signs) get no extra treatment — they're clear
 * on their own. Delimiters already present in the book's labels are excluded from the
 * enclosure pool.
 *
 * Optional `getCanonicalType` maps connectionTypeId to canonical type name for high-priority
 * reference detection. If provided, targets with canonical type 'EIN_MISHPAT' or 
 * 'MESORAH_HASHAS' get rank 0 treatment (simplest, plain).
 * If not provided, high-priority detection is skipped (backward compatibility).
 */
export function buildWordLinkTreatments(
  targets: WordLinkTargetRow[],
  getCanonicalType?: (typeId: number) => string | null,
): Map<number, WordLinkTreatment> {
  const banned = new Set<string>()
  const participants = new Set<number>()
  const highPriorityBooks = new Set<number>()
  const bookOrderIndex = new Map<number, number>()
  
  for (const t of targets) {
    if (labelCarriesOwnSign(t.label)) {
      for (const ch of t.label!) if (!/[א-ת0-9]/.test(ch)) banned.add(ch)
    } else {
      participants.add(t.targetBookId)
      bookOrderIndex.set(t.targetBookId, t.orderIndex)
      // Detect high-priority reference links if the mapping function is provided
      if (getCanonicalType) {
        const canonical = getCanonicalType(t.connectionTypeId)
        if (canonical === 'EIN_MISHPAT' || canonical === 'MESORAH_HASHAS') {
          highPriorityBooks.add(t.targetBookId)
        }
      }
    }
  }
  const enclosures = ENCLOSURE_CANDIDATES.filter(([o, c]) => !banned.has(o) && !banned.has(c))

  const map = new Map<number, WordLinkTreatment>()
  
  // Separate high-priority books from others
  const otherParticipants = [...participants].filter(id => !highPriorityBooks.has(id))
  const orderedHighPriority = [...highPriorityBooks].sort((a, b) => a - b)
  
  // Sort other books by tree order, then by book id
  const orderedOthers = otherParticipants.sort((a, b) => {
    const orderA = bookOrderIndex.get(a) ?? 999
    const orderB = bookOrderIndex.get(b) ?? 999
    if (orderA !== orderB) return orderA - orderB
    return a - b
  })
  
  // High-priority books come first with simplest treatments (plain, no decoration)
  let rank = 0
  for (const bookId of orderedHighPriority) {
    const slot = rank % MARKER_COLOR_BUCKETS
    const treatment: WordLinkTreatment = { bucket: slot }
    map.set(bookId, treatment)
    rank++
  }
  
  // Other books follow in tree order with remaining treatments
  const otherStartRank = rank
  for (const bookId of orderedOthers) {
    const adjustedRank = rank - otherStartRank
    const slot = rank % MARKER_COLOR_BUCKETS
    let treatment: WordLinkTreatment

    if (adjustedRank === 0) {
      // First non-high-priority: plain (no decoration)
      treatment = { bucket: slot }
    } else if (adjustedRank === 1) {
      // Second non-high-priority: underlined
      treatment = { bucket: slot }
      treatment.underline = true
    } else {
      // Slots 2+: enclosures (no decoration)
      const encIndex = adjustedRank - 2
      if (encIndex < enclosures.length) {
        const enc = enclosures[encIndex]!
        treatment = { bucket: slot, open: enc[0], close: enc[1] }
      } else {
        // Ran out of enclosures — cycle back with underline
        treatment = { bucket: slot }
        treatment.underline = true
      }
    }

    map.set(bookId, treatment)
    rank++
  }
  return map
}

/**
 * True when a point-anchor label already carries its own printed reference sign —
 * geresh, gershayim, parentheses, or any other punctuation the source edition used.
 * Such a label is visually distinct as-is and must render verbatim. Bare Hebrew
 * letters and bare digits are NOT signs: every commentary numbers its notes the
 * same way, so those get the app's fallback color/shape treatment (data-wl-c).
 * A missing or blank label (rendered as '§') is likewise identical everywhere — and
 * blank must NOT count as a sign, or the marker would render as nothing at all.
 */
export function labelCarriesOwnSign(label: string | null): boolean {
  return label != null && label.trim() !== '' && !/^[א-ת0-9]+$/.test(label)
}

const pointTag = (a: WordLinkAnchor) => {
  // A label with its own printed sign renders verbatim, untreated.
  if (labelCarriesOwnSign(a.label)) {
    return `<sup class="word-link-marker" data-wl="${wlData(a)}" data-wl-label="${escapeAttr(a.label!)}"></sup>`
  }
  // Slot from the per-book ranking when the loader assigned one (useWordLinkAnchors);
  // the modulo keeps standalone callers deterministic. Enclosure glyphs are chosen at
  // runtime against the book's own sign vocabulary, so they travel inline — main.css
  // deliberately defines no glyph defaults.
  const bucket = a.colorBucket ?? a.targetBookId % MARKER_COLOR_BUCKETS
  const enc =
    a.encOpen != null && a.encClose != null
      ? ` style="--wl-marker-open:'${escapeAttr(a.encOpen)}';--wl-marker-close:'${escapeAttr(a.encClose)}'"`
      : ''
  // '§' (not '°'): the fallback glyph must stay outside the sign vocabulary real DB
  // labels use, so an app-generated mark can never be mistaken for a source edition's
  // own sign.
  const label = a.label?.trim() ? a.label : '§'
  return `<sup class="word-link-marker" data-wl="${wlData(a)}" data-wl-c="${bucket}"${enc} data-wl-label="${escapeAttr(label)}"></sup>`
}

interface AnchorEvent {
  pos: number
  /** Sort order at equal pos: close(0) before point(1) before open(2). */
  kind: 0 | 1 | 2
  anchor: WordLinkAnchor
}

/**
 * Splice word-link markup into raw line HTML at the anchors' visible-char offsets.
 * Well-formedness: an open range-span is closed before every HTML tag and lazily
 * reopened at the next countable char, so the output never mis-nests even when a
 * range crosses tag boundaries. Overlapping ranges are dropped (first wins) —
 * citation anchors don't overlap in practice.
 */
export function applyWordLinkAnchors(content: string, anchors: WordLinkAnchor[]): string {
  if (!anchors.length || !content) return content

  const events: AnchorEvent[] = []
  const sorted = [...anchors].sort((a, b) => a.charStart - b.charStart)
  let lastEnd = -1
  for (const a of sorted) {
    if (a.charEnd != null && a.charEnd > a.charStart) {
      if (a.charStart < lastEnd) continue // overlap — first wins
      events.push({ pos: a.charStart, kind: 2, anchor: a })
      events.push({ pos: a.charEnd, kind: 0, anchor: a })
      lastEnd = a.charEnd
    } else {
      events.push({ pos: a.charStart, kind: 1, anchor: a })
    }
  }
  if (!events.length) return content
  events.sort((a, b) => a.pos - b.pos || a.kind - b.kind)

  const out: string[] = []
  let vis = 0 // visible-char position, upstream convention
  let i = 0
  let eventIndex = 0
  let openAnchor: WordLinkAnchor | null = null // logically inside a range
  let spanOpen = false // a <span class="word-link"> is open in the output

  const flushEvents = () => {
    while (eventIndex < events.length && events[eventIndex]!.pos === vis) {
      const ev = events[eventIndex]!
      if (ev.kind === 0) {
        if (spanOpen) {
          out.push('</span>')
          spanOpen = false
        }
        openAnchor = null
      } else if (ev.kind === 1) {
        out.push(pointTag(ev.anchor))
      } else {
        openAnchor = ev.anchor // span emitted lazily before the next countable char
      }
      eventIndex++
    }
  }

  while (i < content.length) {
    const ch = content[i]!

    if (ch === '<') {
      // Tags count 0 visible chars. Close an open span first so the output stays
      // well-formed; it reopens lazily at the next countable char.
      if (spanOpen) {
        out.push('</span>')
        spanOpen = false
      }
      while (i < content.length) {
        const t = content[i]!
        out.push(t)
        i++
        if (t === '>') break
      }
      continue
    }

    if (ch === '&') {
      // Mirror upstream countVisibleChars exactly: ';' within the next 9 chars →
      // the whole entity is ONE visible char; otherwise the bare '&' counts 1.
      let end = -1
      const lim = Math.min(content.length, i + 10)
      for (let j = i + 1; j < lim; j++) {
        if (content[j] === ';') {
          end = j
          break
        }
      }
      flushEvents()
      if (openAnchor && !spanOpen) {
        out.push(openTag(openAnchor))
        spanOpen = true
      }
      if (end !== -1) {
        for (let j = i; j <= end; j++) out.push(content[j]!)
        i = end + 1
      } else {
        out.push(ch)
        i++
      }
      vis++
      continue
    }

    // Every other char counts 1 — including diacritics (upstream convention).
    flushEvents()
    if (openAnchor && !spanOpen) {
      out.push(openTag(openAnchor))
      spanOpen = true
    }
    out.push(ch)
    vis++
    i++
  }

  flushEvents() // trailing events at pos == total visible count (e.g. close at line end)
  if (spanOpen) out.push('</span>')
  return out.join('')
}

/** Cheap per-line signature for the renderers' annotation cache keys. Anchors are
 * immutable per line within a session (db content), so length alone distinguishes
 * the only transition that matters: not-loaded/empty → loaded. */
export function wordLinkAnchorsSig(anchors: WordLinkAnchor[]): string {
  return anchors.length ? `wl${anchors.length}` : ''
}
