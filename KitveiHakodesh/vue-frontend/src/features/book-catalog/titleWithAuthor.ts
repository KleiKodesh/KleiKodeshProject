// Comparison key. The DB spells acronyms every way — רש"י, רש״י, מהר''ל, רמבם —
// so quote marks (ASCII, gershayim/geresh, typographic) are dropped entirely.
// Measured against seforim.db 2026-10-14; niqqud, maqaf and whitespace never differed.
const matchKey = (s: string) => s.replace(/["'`״׳“”„‘’]/g, '')

// Author fields sometimes carry a "(dates)" suffix the title never repeats.
const stripParens = (s: string) => s.replace(/\s*\([^)]*\)/g, '').trim()

// Author "המאירי" / "הרמב"ן" vs title "מאירי על..." / "חידושי רמב"ן": the
// definite article is dropped in the title. Only for 3+ letters left, so a short
// remainder can't match by accident.
const stripArticle = (s: string) => {
  const bare = s.replace(/^ה/, '')
  return bare.length >= 3 ? bare : s
}

/** Book title with " (author)" appended, as a natural extension of the title —
 *  unless the title already names the author. */
export function titleWithAuthor(title: string, authors?: string | null): string {
  const author = authors?.trim()
  if (!author) return title
  const key = matchKey(title)
  const bare = matchKey(stripParens(author))
  const names = [matchKey(author), bare, stripArticle(bare)].filter(Boolean)
  if (names.some((name) => key.includes(name))) return title
  return `${title} (${author})`
}
