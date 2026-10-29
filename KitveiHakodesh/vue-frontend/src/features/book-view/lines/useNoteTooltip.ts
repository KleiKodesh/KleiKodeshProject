/**
 * Hover preview for a user note, shown in the same panel as the word-link preview
 * (WordLinkTooltip with `interactive: false`). It replaces the native `title`
 * tooltip the note marker used to carry — dropped when the marker became
 * zero-text, since a title attribute is a poor fit for multi-line note text and
 * cannot be styled at all.
 *
 * The text is read straight off the marker's `data-note-text` attribute, which the
 * renderer writes: no fetch, nothing to cache, and no dependence on the note still
 * sitting in the lazy per-line store.
 *
 * Deliberately far simpler than useWordLinkTooltip: a panel the pointer cannot
 * enter needs no grace period, no pointer pinning and no selection guard. Clicking
 * the marker still opens the editable bubble — this only previews it.
 */
import { onScopeDispose, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { escapeHtml } from '@/utils/htmlText'
import type { WordLinkTooltipData } from './useWordLinkTooltip'

const HOVER_DELAY_MS = 250

/**
 * The default reads a user note marker; FastDocView passes its footnote/endnote references
 * (`.note-reference`) and returns the note's rendered HTML (already in the block, no fetch).
 */
export interface NoteTooltipSource {
  selector: string
  /** The panel's HTML for a hovered marker, or null for nothing to preview. */
  htmlFor: (marker: Element) => string | null
}

const USER_NOTE_SOURCE: NoteTooltipSource = {
  selector: '.user-note-marker',
  htmlFor: (el) => {
    const text = el.getAttribute('data-note-text') ?? ''
    // An empty note is one just created and not yet written — nothing to preview.
    // Plain text into an HTML panel: escape it, and keep the author's own line
    // breaks, which are the only structure a note has.
    return text.trim() ? escapeHtml(text).replace(/\r?\n/g, '<br>') : null
  },
}

export function useNoteTooltip(scrollerEl: Ref<HTMLElement | null>, source: NoteTooltipSource = USER_NOTE_SOURCE) {
  const noteTooltip = ref<WordLinkTooltipData | null>(null)
  let hoverEl: Element | null = null
  let hoverTimer: ReturnType<typeof setTimeout> | null = null
  let token = 0

  function clearHoverTimer() {
    if (hoverTimer !== null) {
      clearTimeout(hoverTimer)
      hoverTimer = null
    }
  }

  function closeNoteTooltip() {
    token++
    hoverEl = null
    clearHoverTimer()
    noteTooltip.value = null
  }

  function findMarker(event: Event): Element | null {
    const target = event.target as HTMLElement | null
    const el = target?.closest?.(source.selector)
    return el && scrollerEl.value?.contains(el) ? el : null
  }

  function show(el: Element, forToken: number) {
    if (forToken !== token) return
    const html = source.htmlFor(el)
    if (!html) return
    noteTooltip.value = {
      id: forToken,
      // The panel's heading is for citing another book; a note has no source to cite.
      bookTitle: '',
      tocPath: '',
      html,
      anchorRect: el.getBoundingClientRect(),
    }
  }

  useEventListener(scrollerEl, 'mouseover', (event: MouseEvent) => {
    const el = findMarker(event)
    if (!el || el === hoverEl) return
    hoverEl = el
    clearHoverTimer()
    const forToken = ++token
    hoverTimer = setTimeout(() => {
      hoverTimer = null
      show(el, forToken)
    }, HOVER_DELAY_MS)
  })

  useEventListener(scrollerEl, 'mouseout', (event: MouseEvent) => {
    if (!hoverEl) return
    const related = event.relatedTarget as HTMLElement | null
    if (related && hoverEl.contains(related)) return
    closeNoteTooltip()
  })

  // Clicking the marker opens the editable bubble, which supersedes the preview.
  useEventListener(scrollerEl, 'click', closeNoteTooltip, { capture: true })
  // `scroll` does not bubble, so this is the underlying view moving under the panel.
  useEventListener(scrollerEl, 'scroll', closeNoteTooltip, { passive: true })

  onScopeDispose(clearHoverTimer)

  return { noteTooltip, closeNoteTooltip }
}
