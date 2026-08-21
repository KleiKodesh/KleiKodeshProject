/**
 * Export-time transforms for the DB's built-in word links, used by the
 * "copy with notes" paths (lines view and commentary view).
 *
 * On screen a word link is app chrome: a range citation is a tinted span and a
 * point citation is a CSS-only superscript marker. Neither survives a paste as
 * anything meaningful, so copy-with-notes converts both into things a document
 * can hold:
 *
 *   range citation → the cited words themselves become a real link
 *                    (`<a href="seforimapp://book/…?index=…">the cited text</a>`),
 *                    so the reference rides along inside the sentence.
 *   point citation → an endnote, in its own sequence, keeping the marker the
 *                    reader already sees in the line as its reference mark. The
 *                    entry carries the target line's full text and closes with
 *                    the linked "(book, TOC path)" source.
 *
 * User notes keep their own separate numbered sequence — see the copy menus'
 * extractEndnotes. The two sequences are deliberately independent: each mirrors
 * what the line itself displays.
 *
 * Transforms here run on the DOM rather than by regex: the rendered line already
 * carries highlight/search marks inside a citation span, and matching balanced
 * markup with a regex is exactly where that breaks.
 */
import { buildLineLink } from './useBookViewLineLink'
import { parseWordLinkData, type WordLinkTarget } from './wordLinkAnchors'
import { escapeHtml } from '@/utils/htmlText'

/** Resolved target of one point citation. `html` is the target line's FULL content. */
export interface WordLinkTargetContent {
  html: string
  bookTitle: string
  tocPath: string
}

export interface WordLinkEndnote {
  /** Anchor number — ids only; the reader sees `label`. */
  id: number
  /** The reference mark as displayed in the line (DB label plus any enclosure). */
  label: string
  html: string
  source: string
  link: string
}

/** Inline style for exported links: Word keeps the color, and no underline noise. */
const LINK_STYLE = 'color:var(--accent-color,#0078d4);text-decoration:none'

/** `'['` → `[` — inline custom properties keep their CSS quotes. */
function unquote(value: string): string {
  const trimmed = value.trim()
  return trimmed.replace(/^['"]|['"]$/g, '')
}

/**
 * The mark as the reader sees it: the DB label wrapped in whatever enclosure the
 * per-commentary treatment assigned (see wordLinkAnchors.ts). Falls back to the
 * bare label when the marker carries no enclosure.
 */
function markerLabel(marker: Element): string {
  // A mark must never come out empty: the reference and its endnote back-link would
  // both be invisible, unclickable anchors.
  const label = marker.getAttribute('data-wl-label') || '§'
  const style = marker instanceof HTMLElement ? marker.style : null
  const open = style ? unquote(style.getPropertyValue('--wl-marker-open')) : ''
  const close = style ? unquote(style.getPropertyValue('--wl-marker-close')) : ''
  return `${open}${label}${close}`
}

/** "book, TOC path" — the TOC path is omitted when the target has none. */
function buildSource(target: WordLinkTargetContent): string {
  // TOC paths carry " · " between segments (search-UI display format); a citation
  // should read as one continuous title, so collapse them to spaces — same rule as
  // the copy menus' own flattenTocPath.
  const tocPath = target.tocPath.replace(/\s*·\s*/g, ' ').trim()
  return tocPath ? `${target.bookTitle}, ${tocPath}` : target.bookTitle
}

/**
 * Converts every word link in `html` for export and returns the point-citation
 * endnotes in document order. A point citation whose target could not be resolved
 * (its content was never loaded) is dropped rather than emitted half-formed.
 */
export function applyWordLinkExport(
  html: string,
  resolve: (target: WordLinkTarget) => WordLinkTargetContent | undefined,
): { html: string; endnotes: WordLinkEndnote[] } {
  const root = document.createElement('div')
  root.innerHTML = html

  for (const span of Array.from(root.querySelectorAll('span.word-link'))) {
    const target = parseWordLinkData(span.getAttribute('data-wl'))
    if (!target) continue
    const link = document.createElement('a')
    link.setAttribute('href', buildLineLink(target.bookId, target.lineIndex))
    link.setAttribute('style', LINK_STYLE)
    while (span.firstChild) link.appendChild(span.firstChild)
    span.replaceWith(link)
    // A user-note reference can sit inside the cited words (the note-marker walk
    // does not close an open citation span the way the anchor splicer does). Nested
    // anchors are not representable in HTML — every parser, Word's importer
    // included, closes the outer one at the inner — which would strip the link from
    // the rest of the citation. Lift such a reference out, just after the link.
    for (const nested of Array.from(link.querySelectorAll('a'))) {
      const lift = nested.closest('sup') ?? nested
      if (link.contains(lift)) link.after(lift)
    }
  }

  const endnotes: WordLinkEndnote[] = []
  for (const marker of Array.from(root.querySelectorAll('sup.word-link-marker'))) {
    const target = parseWordLinkData(marker.getAttribute('data-wl'))
    const resolved = target ? resolve(target) : undefined
    if (!target || !resolved) {
      marker.remove()
      continue
    }
    const id = endnotes.length + 1
    const label = markerLabel(marker)
    endnotes.push({
      id,
      label,
      html: resolved.html,
      source: buildSource(resolved),
      link: buildLineLink(target.bookId, target.lineIndex),
    })
    const ref = document.createElement('sup')
    ref.innerHTML =
      `<a href="#wlnote-${id}" id="wlref-${id}" style="${LINK_STYLE}">${escapeHtml(label)}</a>`
    marker.replaceWith(ref)
  }

  return { html: root.innerHTML, endnotes }
}

/**
 * Drops the point-citation markers. They hold no text (the mark is CSS content),
 * so they cannot affect the copied wording either way — this only keeps empty
 * `<sup>` elements out of the pasted document.
 *
 * Range citations are left exactly as they are: their words are the text, and the
 * `span.word-link` around them survives as an inert wrapper carrying a class no
 * document consumer acts on. Unwrapping it would mean a second DOM pass over every
 * plain copy to remove markup that costs the reader nothing.
 */
export function stripWordLinkMarkers(html: string): string {
  return html.replace(/<sup[^>]*class="word-link-marker"[^>]*><\/sup>/g, '')
}

/**
 * The endnote list for point citations. Same footnote shape as the user-note
 * endnotes, with the source as the clickable app link at the end of the entry.
 */
export function buildWordLinkEndnotesHtml(endnotes: WordLinkEndnote[]): string {
  if (!endnotes.length) return ''
  return endnotes
    .map(
      (e) =>
        `<div dir="rtl" id="wlnote-${e.id}">` +
        `<a href="#wlref-${e.id}" style="${LINK_STYLE}">${escapeHtml(e.label)}</a> ` +
        `${e.html} ` +
        `(<a href="${e.link}" style="${LINK_STYLE}">${escapeHtml(e.source)}</a>)` +
        `</div>`,
    )
    .join('\n')
}

/** The `<hr>` the endnote blocks hang under — one rule for both sequences. */
export const ENDNOTES_SEPARATOR =
  '<hr dir="rtl" style="border:none;border-top:1px solid #ccc;margin:8pt 0"/>'
