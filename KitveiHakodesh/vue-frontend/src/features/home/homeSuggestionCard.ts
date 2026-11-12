import type { Component } from 'vue'

/**
 * One card in the home page's single "suggested" collection. Every source —
 * recent documents, frequent folders, today's learning, the navigation
 * destinations — is mapped to this shape, so HomeSuggestionCard.vue renders them
 * all the same way.
 */
export interface HomeSuggestionCard {
  key: string
  title: string
  /** Muted second line: where the card leads (category › book › TOC, a folder path…). */
  path: string
  icon: Component
  color?: string
  /** External destination that can't be reached right now: broken-connection badge. */
  unreachable?: boolean
  open: (openInNewTab: boolean) => void
  /** A remove-only action (no pin), e.g. a UserSpace root. Called at once, no exit
   *  animation: the caller may still ask to confirm. */
  remove?: () => void
  /** A drag handle beside remove (UserSpace roots): its tooltip and its drag events. */
  move?: { title: string; events: Record<string, (e: never) => void> }
  /** Present only on history cards — they can be pinned and removed. */
  pinning?: {
    pinned: boolean
    togglePin: () => void
    remove: () => void
  }
}
