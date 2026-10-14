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

// A title naming the author by surname is recognisable enough: "אבן עזרא על..."
// for author "אברהם אבן עזרא". Any trailing run of 2+ author words, matched as
// whole words (a single word is too loose — first names collide).
function titleHasAuthorTail(titleKey: string, bareAuthor: string): boolean {
  const words = bareAuthor.split(/\s+/).filter(Boolean)
  const titleWords = ` ${titleKey.replace(/\s+/g, ' ')} `
  for (let n = 2; n < words.length; n++) {
    if (titleWords.includes(` ${words.slice(-n).join(' ')} `)) return true
  }
  return false
}

/** Book title with " (author)" appended, as a natural extension of the title —
 *  unless the title already names the author. */
export function titleWithAuthor(title: string, authors?: string | null): string {
  const author = authors?.trim()
  if (!author) return title
  const key = matchKey(title)
  const bare = matchKey(stripParens(author))
  const names = [matchKey(author), bare, stripArticle(bare)].filter(Boolean)
  if (names.some((name) => key.includes(name)) || titleHasAuthorTail(key, bare)) return title
  return `${title} (${author})`
}
