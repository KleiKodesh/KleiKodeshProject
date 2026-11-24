/**
 * כתיב חסר expander — generates plausible כתיב מלא variants of a query
 * by inserting ו and י at every consonant-boundary position.
 *
 * Used only when a dictionary lookup returns no results, to surface
 * "did you mean" suggestions without any DB schema changes.
 *
 * Strategy:
 *   1. Detect and preserve common Hebrew suffixes (ין, ים, ית, ות, ון, ות)
 *      so their vowel letters are not stripped and not used as insertion points.
 *   2. Strip all remaining ו/י from the stem to get the bare consonant skeleton.
 *   3. For each gap between consecutive consonants in the stem, try inserting ו, י, or nothing.
 *   4. Enumerate all combinations (3^gaps variants) — capped at MAX_VARIANTS.
 *   5. Reattach the preserved suffix to every variant.
 *   6. Exclude the original query itself (caller already tried it).
 */

const YOD = 'י'
const VAV = 'ו'

const HEBREW_LETTERS = new Set('אבגדהוזחטיכלמנסעפצקרשת')

const MAX_VARIANTS = 40

// Suffixes to preserve verbatim — ordered longest-first so greedy match works.
// These are grammatical endings whose vowel letters are not root matres lectionis.
const PRESERVED_SUFFIXES = ['ויות', 'יות', 'ות', 'ין', 'ים', 'ית', 'ון', 'ות', 'ה']

function detectSuffix(query: string): { stem: string; suffix: string } {
  for (const suffix of PRESERVED_SUFFIXES) {
    if (query.endsWith(suffix) && query.length > suffix.length) {
      return { stem: query.slice(0, query.length - suffix.length), suffix }
    }
  }
  return { stem: query, suffix: '' }
}

function stripMatres(s: string): string {
  return [...s].filter(c => c !== VAV && c !== YOD).join('')
}

function hebrewPositions(s: string): number[] {
  const positions: number[] = []
  for (let i = 0; i < s.length; i++) {
    if (HEBREW_LETTERS.has(s[i]!)) positions.push(i)
  }
  return positions
}

function applyInsertions(skeleton: string, insertions: [number, string][]): string {
  const sorted = [...insertions].sort((a, b) => a[0] - b[0])
  let result = ''
  let offset = 0
  for (const [pos, char] of sorted) {
    result += skeleton.slice(offset, pos + 1) + char
    offset = pos + 1
  }
  return result + skeleton.slice(offset)
}

/**
 * Generate all כתיב מלא variants of `query` by inserting ו/י at consonant gaps in the stem.
 * Common suffixes (ין, ים, ית, ות, ון) are preserved verbatim and reattached to every variant.
 * The original query is excluded from the result.
 * Result is deduplicated and capped at MAX_VARIANTS.
 */
export function expandKetivHaser(query: string): string[] {
  const { stem, suffix } = detectSuffix(query)
  const skeleton = stripMatres(stem)

  const positions = hebrewPositions(skeleton)
  if (positions.length < 2) return []

  const gaps = positions.length - 1
  const effectiveGaps = Math.min(gaps, 5)
  const gapPositions = positions.slice(0, effectiveGaps)

  const variants = new Set<string>()
  const total = Math.pow(3, effectiveGaps)

  // Always include the bare skeleton + suffix — covers the case where the user typed
  // a מלא form (e.g. שולחן) but the DB headword is the חסר form (e.g. שלחן).
  const bareVariant = skeleton + suffix
  if (bareVariant !== query) variants.add(bareVariant)

  for (let mask = 0; mask < total; mask++) {
    const insertions: [number, string][] = []
    let m = mask
    for (let g = 0; g < effectiveGaps; g++) {
      const choice = m % 3
      m = Math.floor(m / 3)
      if (choice === 1) insertions.push([gapPositions[g]!, VAV])
      else if (choice === 2) insertions.push([gapPositions[g]!, YOD])
    }
    if (insertions.length === 0) continue
    const variant = applyInsertions(skeleton, insertions) + suffix
    if (variant !== query) variants.add(variant)
    if (variants.size >= MAX_VARIANTS) break
  }

  return [...variants]
}
