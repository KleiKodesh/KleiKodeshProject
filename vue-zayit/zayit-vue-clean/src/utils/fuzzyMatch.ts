const QUOTES = /["'״׳]/g
const HAS_QUOTES = /["'״׳]/

/** Levenshtein edit distance between two strings */
export function levenshtein(a: string, b: string): number {
  if (a === b) return 0
  if (a.length === 0) return b.length
  if (b.length === 0) return a.length
  const prev = Array.from({ length: b.length + 1 }, (_, i) => i)
  for (let i = 1; i <= a.length; i++) {
    let cur = i
    for (let j = 1; j <= b.length; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1
      const next = Math.min(prev[j]! + 1, cur + 1, prev[j - 1]! + cost)
      prev[j - 1] = cur
      cur = next
    }
    prev[b.length] = cur
  }
  return prev[b.length]!
}

/**
 * Score a query word against a single path word.
 * Returns 0 for exact match (including quote-stripped equivalence),
 * 1 for fuzzy match (Levenshtein ≤ 1, plain words ≥ 4 chars, no quotes involved),
 * or Infinity for no match.
 */
function scoreWord(qw: string, pw: string): number {
  if (pw === qw || pw.includes(qw)) return 0
  const qwStripped = qw.replace(QUOTES, '')
  const pwStripped = pw.replace(QUOTES, '')
  if (pwStripped === qwStripped || pwStripped.includes(qwStripped)) return 0
  // Never fuzzy-match words that contain quotes — acronyms must match exactly (after stripping)
  if (HAS_QUOTES.test(qw) || HAS_QUOTES.test(pw)) return Infinity
  // Only fuzzy-match plain words of 4+ chars
  if (qwStripped.length < 4) return Infinity
  if (levenshtein(qwStripped, pwStripped) <= 1) return 1
  return Infinity
}

/**
 * Score a query against a path. Returns the total number of fuzzy (non-exact) word matches,
 * or Infinity if any query word has no match at all.
 * Lower score = better match. 0 = all words matched exactly.
 */
export function scoreMatch(path: string, queryWords: string[]): number {
  const pathWords = path.split(/\s+/).filter((w) => w.length > 0)
  let total = 0
  for (const qw of queryWords) {
    const best = Math.min(...pathWords.map((pw) => scoreWord(qw, pw)))
    if (best === Infinity) return Infinity
    total += best
  }
  return total
}

/** Returns true if the path matches all query words (exact or fuzzy) */
export function fuzzyMatchWords(path: string, queryWords: string[]): boolean {
  return scoreMatch(path, queryWords) < Infinity
}

/** Debug: returns which path word matched each query word, and the score. For dev use only. */
export function debugScoreMatch(
  path: string,
  queryWords: string[],
): { qw: string; pw: string; score: number }[] {
  const pathWords = path.split(/\s+/).filter((w) => w.length > 0)
  return queryWords.map((qw) => {
    let best = Infinity
    let bestPw = ''
    for (const pw of pathWords) {
      const s = scoreWord(qw, pw)
      if (s < best) {
        best = s
        bestPw = pw
      }
    }
    return { qw, pw: bestPw, score: best }
  })
}
