// Text carrying cantillation marks renders in the user's teamim font (settings page),
// wherever it shows — not only in the book view, which decides per book (hasTeamim).
// Outside the book view there is no book metadata at hand, so detect by content.
const TEAMIM_CHAR_RE = /[֑-֯]/

const TEAMIM_FONT_STYLE = { fontFamily: 'var(--teamim-text-font)' }

/** Inline style for an element showing `text`: the teamim font when it has teamim. */
export function teamimFontStyle(text: string | null | undefined) {
  return text && TEAMIM_CHAR_RE.test(text) ? TEAMIM_FONT_STYLE : undefined
}
