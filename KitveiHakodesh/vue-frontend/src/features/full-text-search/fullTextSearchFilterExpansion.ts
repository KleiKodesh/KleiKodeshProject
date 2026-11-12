import type { InjectionKey } from 'vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

/**
 * Shared expansion controller for the FTS filter tree.
 *
 * Expansion state lives in a single reactive Set owned by the panel rather than
 * as per-node local state. This lets "expand all" fill the set progressively
 * (batched across animation frames) instead of forcing Vue to mount the entire
 * catalog — thousands of category and book rows — in one blocking synchronous
 * render, which froze the UI.
 */
export interface FilterExpansionController {
  /** Ids of the currently-expanded category nodes (reactive). */
  isExpanded: (id: number) => boolean
  /** Toggle a single node open/closed (used by the per-row expander button). The node, not
   *  its id: a tree whose folders load when opened (user space) reads them from it. */
  toggle: (node: CategoryNode) => void
}

export const FILTER_EXPANSION_KEY: InjectionKey<FilterExpansionController> =
  Symbol('ftsFilterExpansion')
