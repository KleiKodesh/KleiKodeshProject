/**
 * Single source of truth for the copy-menu flag compatibility rules.
 *
 * The book-view copy menu (and the commentary copy menu) expose several copy
 * formatting flags. Some combinations are mutually exclusive — but the rules are
 * NOT arbitrary, and several of the "asymmetries" are intentional. They are
 * documented here so nobody "fixes" a rule that is actually correct.
 *
 * The flags:
 *   sourcePosition        'start' | 'end' | null — put a מקור reference before or
 *                         after the copied text (a plain 3-state radio: start XOR end).
 *   sourceWithQuotation   boolean — collapse the whole selection to ONE inline line
 *                         formatted `(מקור) "ציטוט"`.
 *   withNotes             boolean — convert user note markers to numbered endnotes,
 *                         APPENDED at the very end of the copied text.
 *
 * The exclusivity rules (and WHY each exists):
 *
 *   1. start XOR end.
 *      A single copy can't put the source both before and after the text.
 *
 *   2. withNotes ⊗ end-source.
 *      INTENTIONAL, not an oversight. Endnotes are appended at the END of the
 *      output; end-source also appends `(מקור)` at the END. Both landing at the
 *      end collide and read wrong. withNotes + START-source is fine (source on
 *      top, notes at the bottom — no collision), so that pair is deliberately
 *      ALLOWED. Do not "symmetrise" this by also blocking start-source.
 *
 *   3. sourceWithQuotation ⊗ { start, end, withNotes }.
 *      Quotation collapses everything to one inline `(מקור) "…"` line and returns
 *      early in the builder — it is its own self-contained format. A separate
 *      source decoration (start/end) would be silently discarded by that early
 *      return, and endnotes make no sense on a single inline quote. So quotation
 *      is exclusive with all three.
 *
 * copyJoinLines and copyCleanText are fully independent (no exclusivity) and are
 * intentionally NOT modelled here.
 */

export interface CopyExclusivityFlags {
  copySourcePosition: 'start' | 'end' | null
  copyWithNotes: boolean
  copyAsSourceWithQuotation: boolean
}

/** The flags this model governs — the toggle that changed must be one of these. */
export type CopyExclusivityToggle =
  | 'sourceStart'
  | 'sourceEnd'
  | 'withNotes'
  | 'sourceWithQuotation'

/**
 * Given the current flags and the toggle the user just flipped, return the new
 * flag set with all exclusivity rules enforced. Pure — does not mutate the input.
 *
 * The rules above are applied here in ONE place so the four checkbox handlers
 * (and the two copy menus) can never drift apart or leave a contradictory state
 * reachable. Turning a flag OFF never forces anything else on/off.
 */
export function applyCopyExclusivity(
  current: CopyExclusivityFlags,
  toggle: CopyExclusivityToggle,
  value: boolean,
): CopyExclusivityFlags {
  const next: CopyExclusivityFlags = { ...current }

  switch (toggle) {
    case 'sourceStart':
      // Rule 1 (start XOR end) is inherent: sourcePosition is a single 3-state value.
      next.copySourcePosition = value ? 'start' : null
      // Rule 3: quotation is exclusive with any source position.
      if (value) next.copyAsSourceWithQuotation = false
      // Note: start-source + withNotes is ALLOWED (see rule 2) — do not clear notes.
      break

    case 'sourceEnd':
      next.copySourcePosition = value ? 'end' : null
      if (value) {
        // Rule 2: end-source ⊗ notes (both append at the end).
        next.copyWithNotes = false
        // Rule 3: quotation is exclusive with any source position.
        next.copyAsSourceWithQuotation = false
      }
      break

    case 'withNotes':
      next.copyWithNotes = value
      if (value) {
        // Rule 2: notes ⊗ END-source only. Leave start-source untouched.
        if (next.copySourcePosition === 'end') next.copySourcePosition = null
        // Rule 3: quotation is exclusive with notes.
        next.copyAsSourceWithQuotation = false
      }
      break

    case 'sourceWithQuotation':
      next.copyAsSourceWithQuotation = value
      if (value) {
        // Rule 3: quotation is exclusive with source position AND notes.
        next.copySourcePosition = null
        next.copyWithNotes = false
      }
      break
  }

  return next
}

/**
 * Repairs any contradictory flag combination — e.g. one persisted by an OLDER
 * build before these exclusivity guards existed, which the settings loader reads
 * back verbatim with no validation. Deterministic priority when a saved state
 * violates the rules:
 *   quotation wins over everything (it is a self-contained format),
 *   then end-source drops notes (rule 2).
 * Pure — returns a corrected copy.
 */
export function normalizeCopyFlags(flags: CopyExclusivityFlags): CopyExclusivityFlags {
  const next: CopyExclusivityFlags = { ...flags }

  if (next.copyAsSourceWithQuotation) {
    // Rule 3: quotation is exclusive with source position and notes.
    next.copySourcePosition = null
    next.copyWithNotes = false
  } else if (next.copySourcePosition === 'end' && next.copyWithNotes) {
    // Rule 2: end-source ⊗ notes — keep the source position, drop notes.
    next.copyWithNotes = false
  }

  return next
}
