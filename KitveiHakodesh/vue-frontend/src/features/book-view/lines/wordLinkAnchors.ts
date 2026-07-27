/**
 * Word-level link anchor splicing (SeforimLibrary schema v2 `link_anchor` rows).
 *
 * Range anchors (charEnd != null) wrap the anchored citation text in a clickable
 * `<span class="word-link" data-wl="…">`; point anchors insert an empty
 * `<sup class="word-link-marker" data-wl="…" data-wl-label="…"></sup>` whose label
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
import type { WordLinkAnchor } from '@/webview-host/seforimApi'

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

function escapeAttr(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

const wlData = (a: WordLinkAnchor) => `${a.targetBookId}:${a.targetLineIndex}:${a.targetLineId}`

const openTag = (a: WordLinkAnchor) => `<span class="word-link" data-wl="${wlData(a)}">`

const pointTag = (a: WordLinkAnchor) =>
  `<sup class="word-link-marker" data-wl="${wlData(a)}" data-wl-label="${escapeAttr(a.label ?? '°')}"></sup>`

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
