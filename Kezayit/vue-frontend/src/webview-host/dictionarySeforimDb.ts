// Seforim DB queries for the dictionary feature.
// Queries מצודת ציון, מלבי"ם באור המילות, מחברת מנחם, and ספר הערוך from the main seforim DB.
// Book IDs are looked up at runtime by title pattern and cached — never hardcoded.

import { query as querySeforim } from './seforimDb'

// ── Types ─────────────────────────────────────────────────────────────────────

/** A result row from מצודת ציון or מלבי"ם באור המילות. */
export interface MetzudatRow {
  word:       string
  definition: string
  bookTitle:  string
  bookId:     number
  lineId:     number
  lineIndex:  number
}

/** A result row from מחברת מנחם. */
export interface MenchemRow {
  word:      string        // the matched headword (stripped from tag)
  text:      string        // definition (next line for big-tag; whole line for synonym)
  title:     string | null // section title for synonym lines; null for dictionary entries
  bookId:    number
  lineId:    number
  lineIndex: number
}

/** A result row from ספר הערוך. */
export interface AruchRow {
  word:      string
  text:      string
  bookId:    number
  lineId:    number
  lineIndex: number
}

// ── Book ID cache ─────────────────────────────────────────────────────────────

async function getBookIds(titlePattern: string, cache: { ids: number[] | null }): Promise<number[]> {
  if (cache.ids !== null) return cache.ids
  const rows = await querySeforim<{ id: number }>(
    `SELECT id FROM book WHERE title LIKE ?`,
    [titlePattern]
  )
  cache.ids = rows.map(r => r.id)
  return cache.ids
}

const _metzudatCache = { ids: null as number[] | null }
const _malbimCache   = { ids: null as number[] | null }
const _menchemCache  = { ids: null as number[] | null }
const _aruchCache    = { ids: null as number[] | null }

// ── מצודת ציון / מלבי"ם shared helpers ───────────────────────────────────────

function parseBoldLine(
  content: string, bookTitle: string, bookId: number, lineId: number, lineIndex: number
): MetzudatRow | null {
  const match = content.match(/^<b>([^<]+?)<\/b>\s*(.+)$/)
  if (!match) return null
  const word       = (match[1] ?? '').replace(/\.$/, '').replace(/,$/, '').trim()
  const definition = (match[2] ?? '').replace(/:$/, '').trim()
  if (!word || !definition) return null
  return { word, definition, bookTitle, bookId, lineId, lineIndex }
}

function normalizeHeaderWord(word: string): string {
  return word.replace(/[.,;:״"]/g, '').trim()
}

function headerMatchesExact(headerWord: string, term: string): boolean {
  const normalized = normalizeHeaderWord(headerWord)
  return normalized === term || normalized.split(/[,\s]+/).some(token => token === term)
}

function headerMatchesPrefix(headerWord: string, term: string): boolean {
  const normalized = normalizeHeaderWord(headerWord)
  return normalized.split(/[,\s]+/).some(token => token.startsWith(term) && token !== term)
}

async function queryBoldLines(pattern: string, bookIds: number[]): Promise<MetzudatRow[]> {
  const inClause = bookIds.join(',')
  type LineRow = { content: string; title: string; bookId: number; lineId: number; lineIndex: number }
  const rows = await querySeforim<LineRow>(
    `SELECT l.content, b.title, b.id AS bookId, l.id AS lineId, l.lineIndex
     FROM line l JOIN book b ON b.id = l.bookId
     WHERE l.bookId IN (${inClause})
       AND l.content LIKE ?
     LIMIT 50`,
    [pattern]
  )
  return rows
    .map(r => parseBoldLine(r.content, r.title, r.bookId, r.lineId, r.lineIndex))
    .filter((r): r is MetzudatRow => r !== null)
}

export async function boldExact(term: string, bookIds: number[]): Promise<MetzudatRow[]> {
  if (bookIds.length === 0) return []
  const [plain, withPunctuation] = await Promise.all([
    queryBoldLines(`<b>${term}</b>%`, bookIds),
    queryBoldLines(`<b>${term}%</b>%`, bookIds),
  ])
  const seen = new Set<string>()
  return [...plain, ...withPunctuation].filter(r => {
    if (!headerMatchesExact(r.word, term)) return false
    const key = `${r.bookId}::${r.word}::${r.definition}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export async function boldPrefix(term: string, bookIds: number[]): Promise<MetzudatRow[]> {
  if (bookIds.length === 0) return []
  return (await queryBoldLines(`<b>${term}%</b>%`, bookIds))
    .filter(r => headerMatchesPrefix(r.word, term))
}

export async function boldContains(term: string, bookIds: number[]): Promise<MetzudatRow[]> {
  if (bookIds.length === 0) return []
  return (await queryBoldLines(`<b>%${term}%</b>%`, bookIds))
    .filter(r => {
      const normalized = normalizeHeaderWord(r.word)
      return normalized.split(/[,\s]+/).some(
        token => token.includes(term) && !token.startsWith(term)
      )
    })
}

export async function getMetzudatBookIds(): Promise<number[]> {
  return getBookIds('%מצודת ציון%', _metzudatCache)
}

export async function getMalbimBookIds(): Promise<number[]> {
  return getBookIds('%מלבי%באור המילות%', _malbimCache)
}

// ── מחברת מנחם ───────────────────────────────────────────────────────────────
//
// Two distinct sections:
//
// 1. Dictionary section: <strong><big>HEADWORD</big></strong> followed by definition on next line.
//    → exact match only: term must equal the extracted headword exactly.
//
// 2. Synonym section (early lines): <b>WORD1</b> ... <b>WORD2</b> ...
//    All bold words on a line are synonymous/related.
//    The nearest preceding pure-bold line is the section title.
//    → exact match only: term must equal one of the bold words exactly.

function stripAllHtml(source: string): string {
  return source.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim()
}

function parseBigWord(content: string): string | null {
  const match = content.match(/<big>\s*\u200e?\s*([^\s<]+)\s*<\/big>/)
  return match ? (match[1] ?? '').trim() : null
}

export async function menchemLookup(term: string): Promise<MenchemRow[]> {
  const bookIds = await getBookIds('%מחברת מנחם%', _menchemCache)
  if (bookIds.length === 0) return []
  const bookId = bookIds[0]!

  type RawLine = { id: number; lineIndex: number; content: string }

  // Only query the dictionary section: <strong><big>HEADWORD</big></strong> lines.
  // The early synonym/intro section (before the first <big> entry) is skipped entirely —
  // it is preamble and section headers, not dictionary content.
  // Pattern covers both with and without trailing space before </big>.
  const bigRows = await querySeforim<RawLine>(
    `SELECT id, lineIndex, content FROM line
     WHERE bookId = ? AND (content LIKE ? OR content LIKE ?)
     ORDER BY lineIndex LIMIT 20`,
    [bookId, `%<big>%${term}</big>%`, `%<big>%${term} </big>%`]
  )

  const results: MenchemRow[] = []
  for (const row of bigRows) {
    const word = parseBigWord(row.content)
    if (!word) continue
    const normalized = word.replace(/[.,;:״"]/g, '').trim()
    if (!normalized.includes(term)) continue

    const nextRows = await querySeforim<RawLine>(
      `SELECT id, lineIndex, content FROM line WHERE bookId = ? AND lineIndex = ?`,
      [bookId, row.lineIndex + 1]
    )
    const nextLine = nextRows[0]
    if (!nextLine) continue
    results.push({
      word,
      text:      stripAllHtml(nextLine.content),
      title:     null,
      bookId,
      lineId:    row.id,
      lineIndex: row.lineIndex,
    })
  }

  return results
}

// ── ספר הערוך ─────────────────────────────────────────────────────────────────
//
// Structure: <b><big>HEADWORD</big></b> followed by definition on the same line.
// The headword is wrapped in both <b> and <big> tags.
// → exact match only: term must equal the extracted headword exactly.

function parseBigBoldLine(content: string): { word: string; text: string } | null {
  // Match <b><big>WORD</big></b> followed by the rest of the line
  const match = content.match(/<b><big>([^<]+)<\/big><\/b>\s*(.+)$/)
  if (!match) return null
  const word = (match[1] ?? '').trim()
  const text = (match[2] ?? '').trim()
  if (!word || !text) return null
  return { word, text }
}

export async function aruchLookup(term: string): Promise<AruchRow[]> {
  const bookIds = await getBookIds('%ספר הערוך%', _aruchCache)
  if (bookIds.length === 0) return []
  const bookId = bookIds[0]!

  type RawLine = { id: number; lineIndex: number; content: string }

  // Query lines with <b><big>TERM</big></b> pattern
  // Pattern covers both with and without trailing space before </big>
  const rows = await querySeforim<RawLine>(
    `SELECT id, lineIndex, content FROM line
     WHERE bookId = ? AND (content LIKE ? OR content LIKE ?)
     ORDER BY lineIndex LIMIT 20`,
    [bookId, `%<big>${term}</big>%`, `%<big>${term} </big>%`]
  )

  const results: AruchRow[] = []
  for (const row of rows) {
    const parsed = parseBigBoldLine(row.content)
    if (!parsed) continue
    const normalized = parsed.word.replace(/[.,;:״"]/g, '').trim()
    if (!normalized.includes(term)) continue

    results.push({
      word:      parsed.word,
      text:      stripAllHtml(parsed.text),
      bookId,
      lineId:    row.id,
      lineIndex: row.lineIndex,
    })
  }

  return results
}
