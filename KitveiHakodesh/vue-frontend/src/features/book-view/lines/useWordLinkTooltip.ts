/**
 * Hover preview + click navigation for word-level link markup (`[data-wl]` spans
 * and markers spliced by wordLinkAnchors.ts).
 *
 * Delegated listeners on the scroller (one set per view, not per line):
 *   hover  — 250ms intent delay, then the target line's content is fetched
 *            (module-level cache, one getLineContents round-trip per unique target)
 *            and shown in a WordLinkTooltip anchored to the link element.
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
import { getLineContents } from '@/webview-host/seforimApi'
import { hasActiveTextSelection } from '@/composables/useContextMenuLongPress'
import { parseWordLinkData, type WordLinkTarget } from './wordLinkAnchors'

export interface WordLinkTooltipData {
  /** Unique per hover — used as component key so a new target remounts/re-measures. */
  id: number
  bookTitle: string
  html: string
  anchorRect: DOMRect
}

const HOVER_DELAY_MS = 250

/**
 * How long the tooltip survives the pointer leaving the link. Long enough to
 * cross the MARGIN gap between link and tooltip (and the tooltip's own border),
 * short enough that a pointer moving away feels like an immediate dismissal.
 */
const CLOSE_GRACE_MS = 220

// Target line content, keyed by lineId. Module-level so every view shares it and
// re-hovering a link is instant. Bounded — cleared wholesale when it grows large.
const contentCache = new Map<number, string>()
const CONTENT_CACHE_MAX = 300

export function useWordLinkTooltip(
  scrollerEl: Ref<HTMLElement | null>,
  opts: {
    getBookTitle: (bookId: number) => string
    onNavigate: (target: WordLinkTarget) => void
  },
) {
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
    const el = target?.closest?.('[data-wl]')
    return el && scrollerEl.value?.contains(el) ? el : null
  }

  async function show(el: Element, token: number) {
    const target = parseWordLinkData(el.getAttribute('data-wl'))
    if (!target) return
    let content = contentCache.get(target.lineId)
    if (content == null) {
      try {
        const rows = await getLineContents([target.lineId])
        content = rows[0]?.content ?? ''
      } catch {
        return
      }
      if (contentCache.size >= CONTENT_CACHE_MAX) contentCache.clear()
      contentCache.set(target.lineId, content)
    }
    if (token !== hoverToken || !content) return
    wordLinkTooltip.value = {
      id: token,
      bookTitle: opts.getBookTitle(target.bookId),
      html: content,
      anchorRect: el.getBoundingClientRect(),
    }
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
    const relatedLink = related?.closest?.('[data-wl]')
    if (relatedLink && relatedLink.getAttribute('data-wl') === hoverEl.getAttribute('data-wl')) {
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
    const target = parseWordLinkData(el.getAttribute('data-wl'))
    if (!target) return
    event.preventDefault()
    event.stopPropagation()
    closeWordLinkTooltip()
    opts.onNavigate(target)
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

  // Unmounting mid-delay would otherwise let the hover timer fire for a dead view
  // — and its callback issues a getLineContents round-trip nobody will read.
  onScopeDispose(() => {
    cancelScheduledClose()
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
  })

  return { wordLinkTooltip, closeWordLinkTooltip, keepOpen, releaseOpen, beginSelection }
}
