/**
 * Book path normalization.
 *
 * Transforms book search paths and query strings so that variant spellings,
 * abbreviations, and חסר/מלא spelling differences all resolve to the same tokens.
 *
 * Every rule here is applied symmetrically to both sides — indexed book paths
 * (at catalog load time in bookCatalogTree.ts) and user queries (at search
 * time in useBookCatalogSearch.ts). Never add book-search normalization to
 * normalizeText.ts or inline it in a composable.
 */

// ─── Abbreviation and spelling substitutions ──────────────────────────────────

/**
 * Maps all known title variants to their canonical form.
 * Each entry is [pattern, canonical] where pattern matches every accepted spelling.
 */
const TITLE_VARIANTS: [RegExp, string][] = [
  // שו"ע / שו״ע / שוע → שלחן ערוך
  [/שו["״]?ע/g, 'שלחן ערוך'],
  // שולחן (plene, with vav) → שלחן — standalone word normalization
  [/שולחן/g, 'שלחן'],
]

/**
 * Normalize a book path or search query for matching.
 * Apply to both sides — indexed paths and user input — so they meet at the
 * same canonical form.
 */
export function normalizeBookPath(text: string): string {
  let result = text
  for (const [pattern, canonical] of TITLE_VARIANTS) result = result.replace(pattern, canonical)
  return result
}

// ─── חסר / מלא spelling variant matching ─────────────────────────────────────

const HEBREW_RE = /[\u05d0-\u05ea]/
const YOD = '\u05d9'
const VAV = '\u05d5'

/**
 * A pre-decomposed Hebrew word, computed once at index-build time.
 * Stored alongside each book token so the hot search loop never re-decomposes.
 *
 * - skeleton: the word with all mid-word yod/vav stripped
 * - vowelSet: Set of "position:letter" keys for fast subset checking
 */
export interface DecomposedToken {
  original: string
  skeleton: string
  vowelSet: Set<string>
}

/**
 * Decompose a Hebrew word into its consonantal skeleton and a set of
 * (position, letter) keys for the vowel letters (matres lectionis) stripped.
 *
 * A yod or vav is treated as a vowel letter only when it appears between two
 * Hebrew consonants — never word-initial or word-final.
 *
 * Examples:
 *   נידה   → skeleton "נדה",  vowelSet {"1:י"}
 *   נדה    → skeleton "נדה",  vowelSet {}
 *   שבועות → skeleton "שבעת", vowelSet {"2:ו","3:ו"}
 *   שביעית → skeleton "שבעת", vowelSet {"2:י","3:י"}
 */
export function decomposeHebrewWord(word: string): DecomposedToken {
  const chars = Array.from(word)
  const skeleton: string[] = []
  const vowelSet = new Set<string>()
  let skeletonIndex = 0

  for (let i = 0; i < chars.length; i++) {
    const ch = chars[i]!
    if (
      (ch === YOD || ch === VAV) &&
      i > 0 &&
      i < chars.length - 1 &&
      HEBREW_RE.test(chars[i - 1]!) &&
      HEBREW_RE.test(chars[i + 1]!)
    ) {
      vowelSet.add(`${skeletonIndex}:${ch}`)
    } else {
      skeleton.push(ch)
      skeletonIndex++
    }
  }

  return { original: word, skeleton: skeleton.join(''), vowelSet }
}

/**
 * Returns true if two pre-decomposed Hebrew words are חסר/מלא spelling variants.
 *
 * Two words are variants when:
 *   1. They share the same consonantal skeleton.
 *   2. The vowel sets are compatible: one must be a subset of the other.
 *      נידה ({"1:י"}) matches נדה ({}) — subset.
 *      שבועות ({"2:ו","3:ו"}) does NOT match שביעית ({"2:י","3:י"}) — neither is a subset.
 */
export function areDecomposedVariants(a: DecomposedToken, b: DecomposedToken): boolean {
  if (a.original === b.original) return true
  if (a.skeleton !== b.skeleton) return false
  if (a.vowelSet.size <= b.vowelSet.size) {
    for (const key of a.vowelSet) if (!b.vowelSet.has(key)) return false
    return true
  } else {
    for (const key of b.vowelSet) if (!a.vowelSet.has(key)) return false
    return true
  }
}

/**
 * Convenience wrapper — decompose both words and compare.
 * Use this only when pre-decomposed tokens are not available (e.g. in tests).
 * In the hot search path, use areDecomposedVariants with pre-built tokens instead.
 */
export function areHebrewSpellingVariants(wordA: string, wordB: string): boolean {
  return areDecomposedVariants(decomposeHebrewWord(wordA), decomposeHebrewWord(wordB))
}

// ─── ה prefix normalization ───────────────────────────────────────────────────

const HE = '\u05d4' // ה

/**
 * Strip the definite article ה from the start of a Hebrew word, if present.
 * Returns the stripped form when the remainder is at least 2 characters,
 * otherwise returns null (word is too short to strip meaningfully).
 *
 * Used symmetrically at both index time and query time so that הרמבן and רמבן
 * always resolve to the same lookup key (רמבן).
 *
 * Does NOT check whether the stripped form is a real word — that is intentional.
 * The index stores both the original and the stripped form, so הלכה is still
 * findable as הלכה. A query for לכה simply finds nothing, which is correct.
 */
export function stripHePrefix(word: string): string | null {
  if (word.charCodeAt(0) !== HE.charCodeAt(0)) return null
  const remainder = word.slice(1)
  return remainder.length >= 2 ? remainder : null
}
