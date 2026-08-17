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
 * Scope: per tab AND per panel. The live map is memory, but each panel's state is
 * serialized into its persisted record (see `serializeCommentaryCheckState` at the
 * bottom), so the filter survives tab switches, session restore and reopening a
 * book via "remember my last place". The tab store drops a tab's entry when the
 * tab closes.
 */
import { reactive } from 'vue'
import type { CommentaryCheckStateSnapshot, CommentarySlot } from '../bookViewTypes'

/**
 * Scope key for one panel's check-tree. The two commentary panels of a tab keep
 * entirely separate check state, so every entry is keyed by tab AND slot.
 * `dropForTab`/`prune` below match on the `${tabId}::` prefix to clear both.
 */
export function commentaryScopeKey(tabId: string, slot: CommentarySlot): string {
  return `${tabId}::${slot}`
}

interface ScopeCheckState {
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

const stateByScope = reactive(new Map<string, ScopeCheckState>())

function subKey(sectionLabel: string, subSectionLabel: string): string {
  return `${sectionLabel}::${subSectionLabel}`
}

function ensureState(scopeKey: string): ScopeCheckState {
  let state = stateByScope.get(scopeKey)
  if (!state) {
    state = { sections: new Map(), subsections: new Map(), books: new Map(), expanded: new Set() }
    stateByScope.set(scopeKey, state)
  }
  return state
}

function dropIfEmpty(scopeKey: string): void {
  const state = stateByScope.get(scopeKey)
  if (
    state &&
    state.root === undefined &&
    state.sections.size === 0 &&
    state.subsections.size === 0 &&
    state.books.size === 0 &&
    state.expanded.size === 0
  ) {
    stateByScope.delete(scopeKey)
  }
}

/** Effective default for books under a subsection (ignoring book overrides). */
function nodeDefault(state: ScopeCheckState, sectionLabel: string, subSectionLabel: string): boolean {
  return (
    state.subsections.get(subKey(sectionLabel, subSectionLabel)) ??
    state.sections.get(sectionLabel) ??
    state.root ??
    true
  )
}

// ── Queries ───────────────────────────────────────────────────────────────────

export function isCommentaryBookUnchecked(
  scopeKey: string,
  sectionLabel: string,
  subSectionLabel: string,
  bookId: number,
): boolean {
  const state = stateByScope.get(scopeKey)
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
  scopeKey: string,
  sectionLabel: string,
  subSectionLabel: string,
  bookId: number,
  checked: boolean,
): void {
  const state = ensureState(scopeKey)
  if (checked === nodeDefault(state, sectionLabel, subSectionLabel)) {
    state.books.delete(bookId)
  } else {
    state.books.set(bookId, { path: subKey(sectionLabel, subSectionLabel), checked })
  }
  dropIfEmpty(scopeKey)
}

/**
 * Set a whole section (subSectionLabel null) or subsection. Like toggling the
 * node in a fully-loaded tree: every child — present or future — takes this
 * state, so all child entries under the node are wiped.
 */
export function setCommentaryNodeChecked(
  scopeKey: string,
  sectionLabel: string,
  subSectionLabel: string | null,
  checked: boolean,
): void {
  const state = ensureState(scopeKey)
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
  dropIfEmpty(scopeKey)
}

/** הצג הכל — the root toggle: wipes everything and stores at most one boolean. */
export function setCommentaryAllChecked(scopeKey: string, checked: boolean): void {
  const state = ensureState(scopeKey)
  state.sections.clear()
  state.subsections.clear()
  state.books.clear()
  state.root = checked ? undefined : false
  dropIfEmpty(scopeKey)
}

// ── Expanded/collapsed persistence ────────────────────────────────────────────

export function isCommentaryNodeExpanded(scopeKey: string, nodeKey: string): boolean {
  return stateByScope.get(scopeKey)?.expanded.has(nodeKey) ?? false
}

export function setCommentaryNodeExpanded(scopeKey: string, nodeKey: string, expanded: boolean): void {
  if (expanded) {
    ensureState(scopeKey).expanded.add(nodeKey)
  } else {
    const state = stateByScope.get(scopeKey)
    if (!state) return
    state.expanded.delete(nodeKey)
    dropIfEmpty(scopeKey)
  }
}

// ── Persistence ───────────────────────────────────────────────────────────────

/**
 * Snapshot one panel's check-tree as a plain object. Maps and Sets are not
 * structured-cloneable into IndexedDB, and the live state is a reactive proxy,
 * so everything is copied out into arrays.
 *
 * Returns undefined for the default state (nothing unchecked, nothing expanded) —
 * the common case, which then costs nothing in the saved record.
 *
 * This is deliberately the SOURCE state rather than the derived `isChecked` flags
 * on `visibilityList`: that list only ever holds the current line's books, so it
 * cannot express "this whole section is off" for books that appear on other lines.
 */
export function serializeCommentaryCheckState(scopeKey: string): CommentaryCheckStateSnapshot | undefined {
  const state = stateByScope.get(scopeKey)
  if (!state) return undefined
  return {
    root: state.root,
    sections: [...state.sections],
    subsections: [...state.subsections],
    books: [...state.books].map(([bookId, entry]) => [bookId, entry.path, entry.checked]),
    expanded: [...state.expanded],
  }
}

/**
 * Restore a snapshot into the live map, replacing whatever that scope holds.
 *
 * A missing snapshot clears the scope: an older saved record with no check state,
 * or one saved while everything was checked, must not leave a stale tree behind.
 */
export function hydrateCommentaryCheckState(
  scopeKey: string,
  snapshot: CommentaryCheckStateSnapshot | undefined,
): void {
  if (!snapshot) {
    stateByScope.delete(scopeKey)
    return
  }
  stateByScope.set(scopeKey, {
    root: snapshot.root,
    sections: new Map(snapshot.sections),
    subsections: new Map(snapshot.subsections),
    books: new Map(snapshot.books.map(([bookId, path, checked]) => [bookId, { path, checked }])),
    expanded: new Set(snapshot.expanded),
  })
  dropIfEmpty(scopeKey)
}

/** True once this scope has live state — used to skip a redundant hydrate. */
export function hasCommentaryCheckState(scopeKey: string): boolean {
  return stateByScope.has(scopeKey)
}

// ── Lifecycle (called by the tab store) ───────────────────────────────────────

/** Called by the tab store when a single tab closes. */
export function dropUncheckedCommentaryForTab(tabId: string): void {
  const prefix = `${tabId}::`
  for (const key of stateByScope.keys()) {
    if (key.startsWith(prefix)) stateByScope.delete(key)
  }
}

/** Called by the tab store after bulk tab removal — keeps only live tab ids. */
export function pruneUncheckedCommentary(liveTabIds: Iterable<string>): void {
  const live = new Set(liveTabIds)
  for (const key of stateByScope.keys()) {
    // Keys are `${tabId}::${slot}` - liveness is decided by the tab part alone.
    if (!live.has(key.slice(0, key.indexOf('::')))) stateByScope.delete(key)
  }
}
