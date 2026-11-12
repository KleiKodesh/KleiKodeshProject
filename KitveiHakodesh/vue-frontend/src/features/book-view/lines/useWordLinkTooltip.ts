/**
 * Hover preview + click navigation for word-level link markup (`[data-wl]` spans
 * and markers spliced by wordLinkAnchors.ts, plus the inline `a.ref` citations some
 * databases carry in the line text — see INLINE_REF_SELECTOR).
 *
 * Delegated listeners on the scroller (one set per view, not per line):
 *   hover  — 250ms intent delay, then the target's enclosing TOC section is fetched
 *            (one service call) and shown in a WordLinkTooltip anchored to the link
 *            element, scrolled to the cited line (see SeforimDbWordLinkPreview.cs for why a
 *            section and not just the line). A target whose section cannot be
 *            resolved falls back to its single line. The last few previews are held
 *            for re-hovers while the view lives; nothing is prefetched.
 *   click  — navigates to the target via the caller's onNavigate (opens the book
 *            at the target line). Runs in the CAPTURE phase with stopPropagation
 *            so the line's own click handler (commentary line selection) doesn't
 *            also fire; a drag-select over the link is left alone.
 *
 * Dismissal: pointer leaves BOTH the link and the tooltip, the scroller scrolls,
 * or a link is clicked. Long content scrolls inside the tooltip, so the pointer
 * has to be able to travel from the link into the tooltip and stay there — hence
 * the close is deferred by CLOSE_GRACE_MS rather than firing on mouseout, and the
 * tooltip element reports its own enter/leave through `keepOpen`/`releaseOpen`.
 * The grace period also covers the MARGIN gap the tooltip is positioned with.
 *
 * The preview text is selectable, which outlasts hover: `beginSelection` pins the
 * tooltip open for the whole drag (a selection sweep routinely leaves the box),
 * and a document-level mouseup lifts the pin. Selectability itself is granted in
 * main.css, since the global `* { user-select: none }` reset opts in by selector
 * and this element is Teleported outside all of them.
 */
import { onScopeDispose, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { getLineIndexFromLineId, getWordLinkPreview, type WordLinkPreview } from '@/webview-host/seforimApi'
import { hasActiveTextSelection } from '@/composables/useContextMenuLongPress'
import {
  parseInlineRefLineId,
  parseWordLinkData,
  WORD_LINK_SELECTOR,
  type WordLinkTarget,
} from './wordLinkAnchors'

export interface WordLinkTooltipData {
  /** Unique per hover — used as component key so a new target remounts/re-measures. */
  id: number
  bookTitle: string
  /** Full TOC path of the target line, appended to the title once it resolves. */
  tocPath: string
  html: string
  anchorRect: DOMRect
  /**
   * The target's whole TOC section, when one could be resolved: every line of it,
   * with `focusIndex` marking the cited line. The panel renders these as separate
   * lines and scrolls the cited one into view, so the citation is read in context.
   * Absent for a single-line preview (no TOC, oversized section, or the note
   * tooltip, which has no section at all) — then `html` is the whole content.
   *
   * One optional object rather than two parallel optional fields: lines without a
   * focus index, or an index with no lines, are both meaningless, and keeping them
   * in one object makes those states unrepresentable instead of merely unused.
   */
  section?: {
    lines: { id: number; html: string }[]
    /** Index into `lines` of the cited line — the one scrolled to. */
    focusIndex: number
  }
}

const HOVER_DELAY_MS = 250

/** Previews held for re-hovers while the view lives. */
const RECENT_PREVIEWS = 8

/**
 * How long the tooltip survives the pointer leaving the link. Long enough to
 * cross the MARGIN gap between link and tooltip (and the tooltip's own border),
 * short enough that a pointer moving away feels like an immediate dismissal.
 */
const CLOSE_GRACE_MS = 220

/**
 * What a hover-preview panel previews: which elements are links, what one link's preview holds,
 * and what clicking it does. The book view's word links are one source (useWordLinkTooltip);
 * FastDocView's footnote references are another - same panel, same open/stay/close lifecycle.
 */
export interface LinkPreviewSource {
  selector: string
  /** What identifies one link across its fragments (a range anchor split over tags). */
  linkKey: (el: Element) => string | null
  /** The panel's content for a hovered link, or null for nothing to show. `isCurrent` turns
   *  false once the hover moved on - stop fetching then. */
  preview: (el: Element, isCurrent: () => boolean) => Promise<Omit<WordLinkTooltipData, 'id' | 'anchorRect'> | null>
  /** A plain click on the link (not the end of a selection drag). */
  activate: (el: Element) => void
}

export function useWordLinkTooltip(
  scrollerEl: Ref<HTMLElement | null>,
  opts: {
    getBookTitle: (bookId: number) => string
    onNavigate: (target: WordLinkTarget) => void
  },
) {
  /** The navigation target of a link element. An inline ref names only the line, so its
   *  book and index cost one lookup. */
  async function resolveTarget(el: Element): Promise<WordLinkTarget | null> {
    if (el.hasAttribute('data-wl')) return parseWordLinkData(el.getAttribute('data-wl'))
    const lineId = parseInlineRefLineId(el.getAttribute('href'))
    if (lineId == null) return null
    const row = (await getLineIndexFromLineId(lineId))[0]
    return row ? { bookId: row.bookId, lineIndex: row.lineIndex, lineId } : null
  }

  /** The target line a link names, without a lookup: both forms carry the line id. */
  function targetLineId(el: Element): number | null {
    if (el.hasAttribute('data-wl')) return parseWordLinkData(el.getAttribute('data-wl'))?.lineId ?? null
    return parseInlineRefLineId(el.getAttribute('href'))
  }

  // The last few previews, held while this view lives: hovering back over a link (or another
  // link to the same line) shows it again without a round trip. Not a global cache.
  const recentPreviews = new Map<number, Promise<WordLinkPreview>>()

  function previewFor(lineId: number): Promise<WordLinkPreview> {
    const held = recentPreviews.get(lineId)
    if (held) {
      recentPreviews.delete(lineId)
      recentPreviews.set(lineId, held)
      return held
    }
    const fetched = getWordLinkPreview(lineId)
    recentPreviews.set(lineId, fetched)
    // A failed fetch is not held: the next hover asks again.
    fetched.catch(() => {
      if (recentPreviews.get(lineId) === fetched) recentPreviews.delete(lineId)
    })
    while (recentPreviews.size > RECENT_PREVIEWS) recentPreviews.delete(recentPreviews.keys().next().value!)
    return fetched
  }

  async function preview(el: Element, isCurrent: () => boolean): Promise<Omit<WordLinkTooltipData, 'id' | 'anchorRect'> | null> {
    const lineId = targetLineId(el)
    if (lineId == null) return null
    // One call: the service resolves the line's book, text, TOC path and enclosing TOC
    // section (see SeforimDbWordLinkPreview.cs for why a section) - the TOC itself never ships.
    let found: WordLinkPreview
    try {
      found = await previewFor(lineId)
    } catch {
      return null
    }
    // The cited line is the fallback preview AND the anchor within the section, so an
    // empty one means there is nothing worth showing at all.
    if (!isCurrent() || !found.content) return null
    const section = found.section
    return {
      bookTitle: opts.getBookTitle(found.bookId),
      // The line's full path is the more specific; the section heading stands in when the
      // line has no line_toc row of its own.
      tocPath: found.tocPath || (section ? section.tocText : ''),
      html: found.content,
      // No section (no TOC, oversized, or the cited line not in it): the single line.
      ...(section
        ? {
            section: {
              lines: section.lines.map((l) => ({ id: l.id, html: l.content })),
              focusIndex: section.focusIndex,
            },
          }
        : {}),
    }
  }

  const { linkPreview, closeLinkPreview, keepOpen, releaseOpen, beginSelection } = useLinkPreviewTooltip(scrollerEl, {
    selector: WORD_LINK_SELECTOR,
    // A range anchor split over tags repeats its data-wl; an inline ref is one element, so its href serves.
    linkKey: (el) => el.getAttribute('data-wl') ?? el.getAttribute('href'),
    preview,
    activate: (el) => {
      void resolveTarget(el)
        .then((target) => {
          if (target) opts.onNavigate(target)
        })
        .catch(() => {})
    },
  })
  return { wordLinkTooltip: linkPreview, closeWordLinkTooltip: closeLinkPreview, keepOpen, releaseOpen, beginSelection }
}

/** The hover-preview lifecycle (see the file comment) over any LinkPreviewSource. */
export function useLinkPreviewTooltip(scrollerEl: Ref<HTMLElement | null>, source: LinkPreviewSource) {
  const wordLinkTooltip = ref<WordLinkTooltipData | null>(null)
  let hoverToken = 0
  let hoverTimer: ReturnType<typeof setTimeout> | null = null
  let hoverEl: Element | null = null
  let closeTimer: ReturnType<typeof setTimeout> | null = null
  // The pointer is inside the tooltip itself — it must not close on any schedule
  // until it leaves again (the user may be reading or dragging its scrollbar).
  let pointerInTooltip = false
  // A text-selection drag that STARTED inside the tooltip. A drag easily strays
  // past the tooltip's edge, and losing the preview mid-selection would drop the
  // selection with it — so nothing may close while this is set.
  let selectingInTooltip = false

  function cancelScheduledClose() {
    if (closeTimer !== null) {
      clearTimeout(closeTimer)
      closeTimer = null
    }
  }

  /**
   * Unconditional close. Scroll, click-through and host callers all route here —
   * the selection pin deliberately does NOT gate it: an explicit dismissal must
   * always win, or one stuck flag would strand the tooltip on screen forever.
   */
  function closeWordLinkTooltip() {
    hoverToken++
    hoverEl = null
    pointerInTooltip = false
    selectingInTooltip = false
    cancelScheduledClose()
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
    wordLinkTooltip.value = null
  }

  /**
   * True while a custom context menu is on screen. Right-clicking the preview
   * opens one, and reaching "העתק" means leaving the tooltip — which would take
   * the very text the menu is about to copy with it. Queried live rather than
   * tracked, so a menu dismissed by any route (outside click, window blur,
   * running an item) needs no bookkeeping here.
   */
  function contextMenuOpen(): boolean {
    return document.querySelector('.context-menu') !== null
  }

  /**
   * Close after a grace period, unless the pointer reaches the tooltip (or
   * returns to the link) first. Re-entering either one cancels the pending close.
   *
   * `pointerInTooltip` and `selectingInTooltip` each have a release event that
   * re-arms this, so bailing on them is final. An open context menu has no such
   * event — it can close by outside click, window blur or running an item, and
   * only some of those reach us — so that branch re-arms instead of giving up, or
   * a preview would sit there indefinitely once the menu went away.
   */
  function scheduleClose() {
    cancelScheduledClose()
    closeTimer = setTimeout(() => {
      closeTimer = null
      if (pointerInTooltip || selectingInTooltip) return
      if (contextMenuOpen()) {
        scheduleClose()
        return
      }
      closeWordLinkTooltip()
    }, CLOSE_GRACE_MS)
  }

  /** The tooltip element reports the pointer entering it. */
  function keepOpen() {
    pointerInTooltip = true
    cancelScheduledClose()
  }

  /**
   * The tooltip element reports the pointer leaving it. The pointer may be on its
   * way to a context menu opened from the preview; scheduleClose's own gate keeps
   * the tooltip alive for as long as that menu stays open.
   */
  function releaseOpen() {
    pointerInTooltip = false
    scheduleClose()
  }

  /**
   * A left-button mousedown inside the tooltip — the start of a possible selection
   * drag. Held until mouseup anywhere, then the pointer's real position decides.
   *
   * Only the left button, because only it sweeps a selection: a right-click takes
   * no pin (its mouseup is the one that opens the context menu and must not be
   * read as "drag finished"), and a middle-click autoscroll is not a selection.
   * Pinning on those stranded the preview — nothing then cleared the flag.
   */
  function beginSelection() {
    selectingInTooltip = true
    cancelScheduledClose()
  }

  function findLinkEl(event: Event): Element | null {
    const target = event.target as HTMLElement | null
    const el = target?.closest?.(source.selector)
    return el && scrollerEl.value?.contains(el) ? el : null
  }

  const linkKey = source.linkKey

  async function show(el: Element, token: number) {
    const content = await source.preview(el, () => token === hoverToken).catch(() => null)
    if (!content || token !== hoverToken) return
    wordLinkTooltip.value = { ...content, id: token, anchorRect: el.getBoundingClientRect() }
  }

  function onMouseOver(event: MouseEvent) {
    const el = findLinkEl(event)
    if (!el) return
    // Returning to the same link during the grace period keeps the open tooltip.
    if (el === hoverEl) {
      cancelScheduledClose()
      return
    }
    hoverEl = el
    cancelScheduledClose()
    if (hoverTimer !== null) clearTimeout(hoverTimer)
    const token = ++hoverToken
    hoverTimer = setTimeout(() => {
      hoverTimer = null
      void show(el, token)
    }, HOVER_DELAY_MS)
  }

  function onMouseOut(event: MouseEvent) {
    if (!hoverEl) return
    const related = event.relatedTarget as HTMLElement | null
    if (related && hoverEl.contains(related)) return
    // A range anchor crossing tag boundaries is emitted as several fragments
    // sharing one data-wl, so compare the value, not element identity — moving
    // between fragments of the same link never leaves it.
    const relatedLink = related?.closest?.(source.selector)
    if (relatedLink && linkKey(relatedLink) === linkKey(hoverEl)) {
      hoverEl = relatedLink
      return
    }
    // Moving straight into the tooltip must not dismiss it — it is teleported to
    // body, so it is never a descendant of the scroller and would otherwise read
    // as "left the link". Defer instead of closing, and let the tooltip's own
    // mouseenter cancel it.
    // Only defer here — do NOT pin. The tooltip's own mouseenter sets the pin and
    // its mouseleave releases it; pinning from this side predicts a mouseenter
    // that may never arrive, and nothing would then ever clear the flag.
    if (related?.closest?.('.word-link-tooltip')) {
      cancelScheduledClose()
      scheduleClose()
      return
    }
    // Heading into the context menu opened from this preview — same reasoning as
    // moving into the tooltip: the menu's copy action needs the text to survive.
    if (related?.closest?.('.context-menu')) {
      cancelScheduledClose()
      return
    }
    // No tooltip on screen yet (still inside the hover delay): nothing to keep
    // open, so drop the pending fetch immediately — unless a selection drag is
    // under way, which owns the tooltip until the button comes up.
    if (!wordLinkTooltip.value && !selectingInTooltip) {
      closeWordLinkTooltip()
      return
    }
    scheduleClose()
  }

  function onClick(event: MouseEvent) {
    if (event.button !== 0) return
    const el = findLinkEl(event)
    if (!el) return
    // A drag to select text ends with a click too — don't hijack it (same guard
    // as the line-click handler).
    if (hasActiveTextSelection()) return
    // Claimed synchronously: an inline ref's target resolves after an await, by which
    // time the browser would already have followed its seforim:// href.
    event.preventDefault()
    event.stopPropagation()
    closeWordLinkTooltip()
    source.activate(el)
  }

  useEventListener(scrollerEl, 'mouseover', onMouseOver)
  useEventListener(scrollerEl, 'mouseout', onMouseOut)
  useEventListener(scrollerEl, 'click', onClick, { capture: true })
  // `scroll` does not bubble, so scrolling the tooltip's own overflow never
  // reaches this — only the underlying view scrolling dismisses the preview.
  useEventListener(scrollerEl, 'scroll', closeWordLinkTooltip, { passive: true })

  // The drag can be released anywhere, so this has to be on the document. Once
  // the button is up the pin lifts and the pointer's actual position decides:
  // still inside means stay, outside means close on the usual grace period.
  // Deliberately unconditional on the button — only a left press takes the pin,
  // so any release ends that drag, and skipping some buttons here is what left
  // the flag set forever after a right-click.
  useEventListener(() => document, 'mouseup', () => {
    if (!selectingInTooltip) return
    selectingInTooltip = false
    if (!pointerInTooltip) scheduleClose()
  })

  // Once the context menu is gone the pointer is often already outside the
  // tooltip, and no further mouse event would arrive to re-evaluate the close —
  // the preview would hang around. A click anywhere dismisses the menu, so
  // re-check on the next one; by then `.context-menu` is detached and the usual
  // gate lets the close through.
  useEventListener(() => document, 'click', () => {
    if (!wordLinkTooltip.value || pointerInTooltip || contextMenuOpen()) return
    scheduleClose()
  })

  // Escape dismisses the preview. On the document, because the panel is teleported
  // out of the scroller and takes no focus, so a scoped listener would never see the
  // key. Gated on a preview actually being open and NOT stopped from propagating:
  // Escape is heavily overloaded here (search bars, bubbles, menus), and a preview
  // is the shallowest thing on screen — swallowing the key would strand whatever
  // else the user meant to close.
  useEventListener(() => document, 'keydown', (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || !wordLinkTooltip.value) return
    closeWordLinkTooltip()
  })

  // Unmounting mid-delay would otherwise let the hover timer fire for a dead view
  // — and its callback issues two round-trips nobody will read.
  onScopeDispose(() => {
    cancelScheduledClose()
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
  })

  /**
   * Opens the preview of `el` now, without a hover - e.g. a search hit on a footnote pops
   * its note open on arrival. It then closes like any preview (scroll, click, Escape).
   */
  function openLinkPreview(el: Element) {
    closeWordLinkTooltip()
    hoverEl = el
    void show(el, ++hoverToken)
  }

  return { linkPreview: wordLinkTooltip, closeLinkPreview: closeWordLinkTooltip, keepOpen, releaseOpen, beginSelection, openLinkPreview }
}
