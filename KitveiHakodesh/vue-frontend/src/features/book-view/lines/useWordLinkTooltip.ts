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
 * Dismissal: pointer leaves the link, scroller scrolls, or a link is clicked.
 */
import { ref, type Ref } from 'vue'
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

  function closeWordLinkTooltip() {
    hoverToken++
    hoverEl = null
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
    wordLinkTooltip.value = null
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
    if (!el || el === hoverEl) return
    hoverEl = el
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
    if (related?.closest?.('[data-wl]') === hoverEl) return
    closeWordLinkTooltip()
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
  useEventListener(scrollerEl, 'scroll', closeWordLinkTooltip, { passive: true })

  return { wordLinkTooltip, closeWordLinkTooltip }
}
