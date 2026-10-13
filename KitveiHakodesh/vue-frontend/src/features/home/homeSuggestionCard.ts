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
  open: (openInNewTab: boolean) => void
  /** Present only on history cards — they can be pinned and removed. */
  pinning?: {
    pinned: boolean
    togglePin: () => void
    remove: () => void
  }
}
