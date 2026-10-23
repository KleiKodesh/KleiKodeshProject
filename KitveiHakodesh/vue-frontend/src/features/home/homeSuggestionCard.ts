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
  /**
   * Optional tag line under the path (the source library of a catalog item). Its
   * own single line — never merged into `path` — so a long or Latin-script name
   * cannot change the card's height or bleed into the lines around it.
   */
  badge?: string
  /** Keep the badge line even when this card has none, so mixed cards stay one height. */
  reserveBadge?: boolean
  /**
   * Fixed-height mode: `path` is ONE line that is clipped with an ellipsis rather
   * than wrapped. Required wherever cards sit in a grid whose width can change
   * after the first layout (a scrollbar appearing): wrapped text changes a card's
   * height after the grid has sized its rows, and the cards then overlap.
   */
  singleLine?: boolean
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
