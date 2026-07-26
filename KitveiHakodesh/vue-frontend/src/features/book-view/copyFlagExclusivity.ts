/**
 * Single source of truth for the copy-menu flag compatibility rules.
 *
 * The book-view copy menu (and the commentary copy menu) expose several copy
 * formatting flags. Some combinations are constrained — but the rules are NOT
 * arbitrary, and several of the "asymmetries" are intentional. They are documented
 * here so nobody "fixes" a rule that is actually correct.
 *
 * The flags:
 *   sourcePosition        'start' | 'end' | null — put a מקור reference before or
 *                         after the copied text (a plain 3-state radio: start XOR end).
 *   sourceWithQuotation   boolean — collapse the whole selection to ONE inline line
 *                         formatted as a quote with the source in parentheses. The
 *                         SOURCE POSITION decides where the parenthesised מקור goes:
 *                           start → `(מקור) "ציטוט"`
 *                           end   → `"ציטוט" (מקור)`
 *   withNotes             boolean — convert user note markers to numbered endnotes,
 *                         APPENDED at the very end of the copied text.
 *
 * The rules (and WHY each exists):
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
 *   3. sourceWithQuotation REQUIRES a source position, and ⊗ withNotes.
 *      Quotation is a self-contained one-line format; the source position is not
 *      an incompatible option here but the very thing that chooses its layout
 *      (parenthesised מקור at start vs. end). So whenever quotation is ON, exactly
 *      one of start/end is ON too — turning quotation on defaults to 'start' when
 *      no position is set, and while it is on the two positions behave as a strict
 *      radio (you can switch between them but never turn both off). Endnotes still
 *      make no sense on a single inline quote, so quotation ⊗ withNotes remains.
 *
 * copyJoinLines and copyCleanText are fully independent (no rules) and are
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

/** The position quotation falls back to when it is turned on with no position set. */
const DEFAULT_QUOTATION_POSITION = 'start' as const

/**
 * Given the current flags and the toggle the user just flipped, return the new
 * flag set with all rules enforced. Pure — does not mutate the input.
 *
 * The rules above are applied here in ONE place so the four checkbox handlers
 * (and the two copy menus) can never drift apart or leave an invalid state
 * reachable.
 */
export function applyCopyExclusivity(
  current: CopyExclusivityFlags,
  toggle: CopyExclusivityToggle,
  value: boolean,
): CopyExclusivityFlags {
  const next: CopyExclusivityFlags = { ...current }

  switch (toggle) {
    case 'sourceStart':
      if (value) {
        // Rule 1 (start XOR end) is inherent: sourcePosition is a single 3-state value.
        next.copySourcePosition = 'start'
        // Note: start-source + withNotes is ALLOWED (see rule 2) — do not clear notes.
      } else {
        // Rule 3: while quotation is on a position is required, so unchecking the
        // active position flips to the other rather than clearing it.
        next.copySourcePosition = next.copyAsSourceWithQuotation ? 'end' : null
      }
      break

    case 'sourceEnd':
      if (value) {
        next.copySourcePosition = 'end'
        // Rule 2: end-source ⊗ notes (both append at the end).
        next.copyWithNotes = false
      } else {
        // Rule 3: flip to the other position while quotation is on (never both off).
        next.copySourcePosition = next.copyAsSourceWithQuotation ? 'start' : null
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
        // Rule 3: quotation requires a position (default start) and excludes notes.
        if (next.copySourcePosition == null) next.copySourcePosition = DEFAULT_QUOTATION_POSITION
        next.copyWithNotes = false
      }
      // Turning quotation OFF frees the source position to be independent again;
      // leave it as-is (start/end still a plain decoration on its own).
      break
  }

  return next
}

/**
 * Repairs any invalid flag combination — e.g. one persisted by an OLDER build with
 * different rules, which the settings loader reads back verbatim with no validation.
 * Deterministic repair:
 *   - quotation on with notes → drop notes (rule 3),
 *   - quotation on with no position → default to 'start' (rule 3),
 *   - otherwise end-source with notes → drop notes (rule 2).
 * Pure — returns a corrected copy.
 */
export function normalizeCopyFlags(flags: CopyExclusivityFlags): CopyExclusivityFlags {
  const next: CopyExclusivityFlags = { ...flags }

  if (next.copyAsSourceWithQuotation) {
    // Rule 3: quotation excludes notes and requires a position.
    next.copyWithNotes = false
    if (next.copySourcePosition == null) next.copySourcePosition = DEFAULT_QUOTATION_POSITION
  } else if (next.copySourcePosition === 'end' && next.copyWithNotes) {
    // Rule 2: end-source ⊗ notes — keep the source position, drop notes.
    next.copyWithNotes = false
  }

  return next
}
