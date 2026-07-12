/**
 * Per-tab virtual check-tree for the commentary filter.
 *
 * The filter tree (section → subsection → book) only ever shows the books of
 * the CURRENT line, but check state must behave as if the full tree of the
 * whole corpus were loaded all the time:
 *
 * - Toggling a parent cascades to all children — including books that only
 *   appear on other lines — by storing a DEFAULT for that category and wiping
 *   every child entry under it (so category toggles shrink the stored state).
 * - After that, individual children can still be toggled and their state
 *   persists (e.g. uncheck ציונים, then re-check one book inside it — that
 *   book stays checked on every line, while the rest of ציונים stays hidden).
 *
 * Resolution for a book: own override → subsection default → section default
 * → root default (הצג הכל) → checked. Entries equal to their parent default are
 * deleted rather than stored, so memory stays minimal ("check all" clears the
 * whole tab entry; "uncheck all" stores a single boolean).
 *
 * Expanded/collapsed state of tree nodes is kept here too, so it survives line
 * changes and tab switches.
 *
 * Scope: per tab, in-memory only — never persisted, resets on app start. The
 * tab store drops a tab's entry when the tab closes.
 */
import { reactive } from 'vue'

interface TabCheckState {
  /** הצג הכל default; undefined = checked. */
  root?: boolean
  /** sectionLabel → default for everything under it. */
  sections: Map<string, boolean>
  /** `${sectionLabel}::${subSectionLabel}` → default for books under it. */
  subsections: Map<string, boolean>
  /** Individual book overrides; path = `${sectionLabel}::${subSectionLabel}` for cleanup. */
  books: Map<number, { path: string; checked: boolean }>
  /** Expanded tree nodes, by sectionLabel or `${sectionLabel}::${subSectionLabel}`. */
  expanded: Set<string>
}

const stateByTab = reactive(new Map<string, TabCheckState>())

function subKey(sectionLabel: string, subSectionLabel: string): string {
  return `${sectionLabel}::${subSectionLabel}`
}

function ensureState(tabId: string): TabCheckState {
  let state = stateByTab.get(tabId)
  if (!state) {
    state = { sections: new Map(), subsections: new Map(), books: new Map(), expanded: new Set() }
    stateByTab.set(tabId, state)
  }
  return state
}

function dropIfEmpty(tabId: string): void {
  const state = stateByTab.get(tabId)
  if (
    state &&
    state.root === undefined &&
    state.sections.size === 0 &&
    state.subsections.size === 0 &&
    state.books.size === 0 &&
    state.expanded.size === 0
  ) {
    stateByTab.delete(tabId)
  }
}

/** Effective default for books under a subsection (ignoring book overrides). */
function nodeDefault(state: TabCheckState, sectionLabel: string, subSectionLabel: string): boolean {
  return (
    state.subsections.get(subKey(sectionLabel, subSectionLabel)) ??
    state.sections.get(sectionLabel) ??
    state.root ??
    true
  )
}

// ── Queries ───────────────────────────────────────────────────────────────────

export function isCommentaryBookUnchecked(
  tabId: string,
  sectionLabel: string,
  subSectionLabel: string,
  bookId: number,
): boolean {
  const state = stateByTab.get(tabId)
  if (!state) return false
  const override = state.books.get(bookId)
  if (override !== undefined) return !override.checked
  return !nodeDefault(state, sectionLabel, subSectionLabel)
}

// ── Toggles ───────────────────────────────────────────────────────────────────

/**
 * Set one book's state. Stored as an override only when it differs from the
 * effective parent default — a book re-checked under an unchecked category
 * persists as checked on every line; one matching its surroundings costs nothing.
 */
export function setCommentaryBookChecked(
  tabId: string,
  sectionLabel: string,
  subSectionLabel: string,
  bookId: number,
  checked: boolean,
): void {
  const state = ensureState(tabId)
  if (checked === nodeDefault(state, sectionLabel, subSectionLabel)) {
    state.books.delete(bookId)
  } else {
    state.books.set(bookId, { path: subKey(sectionLabel, subSectionLabel), checked })
  }
  dropIfEmpty(tabId)
}

/**
 * Set a whole section (subSectionLabel null) or subsection. Like toggling the
 * node in a fully-loaded tree: every child — present or future — takes this
 * state, so all child entries under the node are wiped.
 */
export function setCommentaryNodeChecked(
  tabId: string,
  sectionLabel: string,
  subSectionLabel: string | null,
  checked: boolean,
): void {
  const state = ensureState(tabId)
  if (subSectionLabel == null) {
    const prefix = `${sectionLabel}::`
    for (const key of state.subsections.keys()) {
      if (key.startsWith(prefix)) state.subsections.delete(key)
    }
    for (const [bookId, entry] of state.books) {
      if (entry.path.startsWith(prefix)) state.books.delete(bookId)
    }
    if (checked === (state.root ?? true)) state.sections.delete(sectionLabel)
    else state.sections.set(sectionLabel, checked)
  } else {
    const key = subKey(sectionLabel, subSectionLabel)
    for (const [bookId, entry] of state.books) {
      if (entry.path === key) state.books.delete(bookId)
    }
    if (checked === (state.sections.get(sectionLabel) ?? state.root ?? true)) state.subsections.delete(key)
    else state.subsections.set(key, checked)
  }
  dropIfEmpty(tabId)
}

/** הצג הכל — the root toggle: wipes everything and stores at most one boolean. */
export function setCommentaryAllChecked(tabId: string, checked: boolean): void {
  const state = ensureState(tabId)
  state.sections.clear()
  state.subsections.clear()
  state.books.clear()
  state.root = checked ? undefined : false
  dropIfEmpty(tabId)
}

// ── Expanded/collapsed persistence ────────────────────────────────────────────

export function isCommentaryNodeExpanded(tabId: string, nodeKey: string): boolean {
  return stateByTab.get(tabId)?.expanded.has(nodeKey) ?? false
}

export function setCommentaryNodeExpanded(tabId: string, nodeKey: string, expanded: boolean): void {
  if (expanded) {
    ensureState(tabId).expanded.add(nodeKey)
  } else {
    const state = stateByTab.get(tabId)
    if (!state) return
    state.expanded.delete(nodeKey)
    dropIfEmpty(tabId)
  }
}

// ── Lifecycle (called by the tab store) ───────────────────────────────────────

/** Called by the tab store when a single tab closes. */
export function dropUncheckedCommentaryForTab(tabId: string): void {
  stateByTab.delete(tabId)
}

/** Called by the tab store after bulk tab removal — keeps only live tab ids. */
export function pruneUncheckedCommentary(liveTabIds: Iterable<string>): void {
  const live = new Set(liveTabIds)
  for (const tabId of stateByTab.keys()) {
    if (!live.has(tabId)) stateByTab.delete(tabId)
  }
}
