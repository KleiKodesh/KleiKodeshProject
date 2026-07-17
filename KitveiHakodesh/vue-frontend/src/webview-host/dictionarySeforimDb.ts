// Seforim DB queries for the dictionary feature.
// Queries מצודת ציון, מלבי"ם באור המילות, מחברת מנחם, מיקרופדיה, and ספר הערוך from the main seforim DB.
// Book IDs are looked up at runtime by title pattern and cached — never hardcoded.

import {
  getBookIdsByTitlePattern,
  getBookIdByExactTitle,
  getLinesWithContentPatternForBooks,
  getLinesWithEitherContentPattern,
  getLineByBookAndLineIndex,
} from './seforimApi'

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

/** A result row from מיקרופדיה (an encyclopedia entry — the הגדרה gloss). */
export interface MicropediaRow {
  word:      string   // the entry headword (the <h2> text)
  text:      string   // the הגדרה gloss (concise definition), HTML stripped
  bookId:    number
  lineId:    number   // the <h2> header line id
  lineIndex: number   // the <h2> header lineIndex — used for Ctrl+click navigation
}

// ── Book ID cache ─────────────────────────────────────────────────────────────

async function getBookIds(titlePattern: string, cache: { ids: number[] | null }): Promise<number[]> {
  if (cache.ids !== null) return cache.ids
  const rows = await getBookIdsByTitlePattern(titlePattern)
  cache.ids = rows.map(r => r.id)
  return cache.ids
}

const _metzudatCache   = { ids: null as number[] | null }
const _malbimCache     = { ids: null as number[] | null }
const _menchemCache    = { ids: null as number[] | null }
const _aruchCache      = { ids: null as number[] | null }
const _micropediaCache = { ids: null as number[] | null }

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
  const rows = await getLinesWithContentPatternForBooks(bookIds, pattern)
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

  // Only query the dictionary section: <strong><big>HEADWORD</big></strong> lines.
  // The early synonym/intro section (before the first <big> entry) is skipped entirely —
  // it is preamble and section headers, not dictionary content.
  // Pattern covers both with and without trailing space before </big>.
  const bigRows = await getLinesWithEitherContentPattern(
    bookId, `%<big>%${term}</big>%`, `%<big>%${term} </big>%`,
  )

  const results: MenchemRow[] = []
  for (const row of bigRows) {
    const word = parseBigWord(row.content)
    if (!word) continue
    const normalized = word.replace(/[.,;:״"]/g, '').trim()
    if (!normalized.includes(term)) continue

    const nextRows = await getLineByBookAndLineIndex(bookId, row.lineIndex + 1)
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

// ── מיקרופדיה ────────────────────────────────────────────────────────────────
//
// An encyclopedia: each entry (ערך) is an <h2>HEADWORD</h2> heading line, and its
// content spans the following lines (paragraphs, <h3>/<h4> subsections, lists) up
// to the next <h2>. We surface only the concise הגדרה (definition) gloss — the
// full ערך is one Ctrl+click away in the book view.
//
// Every entry opens with a `<b>הגדרה</b>` marker; the gloss text follows it, either
// on the same line or on the next line. A minority of entries lack the marker — for
// those we fall back to the first body line after the <h2>.
//
// → match: exact headword (<h2>TERM</h2>) plus comma-qualified forms (<h2>TERM, …).

function stripLeadingDash(text: string): string {
  return text.replace(/^[\s‎ ]*[-–—]\s*/, '').trim()
}

/** Extract the gloss text from a body line, cutting anything from the first block tag on. */
function extractGlossText(content: string): string {
  // Everything up to the first structural block tag (subsection heading, table, list).
  const beforeBlock = content.split(/<(?:h[3-6]|div|ul|ol|table)\b/i)[0] ?? content
  return stripLeadingDash(stripAllHtml(beforeBlock))
}

/**
 * Given a matched <h2> entry, resolve its הגדרה gloss. Fetches at most the two body
 * lines after the header: the one carrying the `<b>הגדרה</b>` marker, and (when the
 * gloss sits on the following line) the next one.
 */
async function resolveMicropediaGloss(bookId: number, headerLineIndex: number): Promise<string> {
  const firstRows = await getLineByBookAndLineIndex(bookId, headerLineIndex + 1)
  const first = firstRows[0]
  if (!first) return ''

  if (first.content.includes('הגדרה')) {
    // Drop the `<b>הגדרה</b>` marker and any trailing `<small>N</small>` footnote index.
    const afterMarker = first.content
      .replace(/^[\s\S]*?הגדרה<\/b>/, '')
      .replace(/^\s*<small\b[^>]*>[^<]*<\/small>/, '')
    const gloss = extractGlossText(afterMarker)
    if (gloss) return gloss

    // Gloss is on the next line.
    const nextRows = await getLineByBookAndLineIndex(bookId, headerLineIndex + 2)
    const next = nextRows[0]
    if (next && !next.content.trim().startsWith('<h')) return extractGlossText(next.content)
    return ''
  }

  // No הגדרה marker — use the first body line (skip subsection headings).
  if (first.content.trim().startsWith('<h')) return ''
  return extractGlossText(first.content)
}

export async function micropediaLookup(term: string): Promise<MicropediaRow[]> {
  if (_micropediaCache.ids === null) {
    const rows = await getBookIdByExactTitle('מיקרופדיה')
    _micropediaCache.ids = rows.map(r => r.id)
  }
  if (_micropediaCache.ids.length === 0) return []
  const bookId = _micropediaCache.ids[0]!

  // Match the entry headword exactly (<h2>TERM</h2>) or as a comma-qualified form
  // (<h2>TERM, …</h2> — e.g. 'אב, המוליד' for the term 'אב').
  const rows = await getLinesWithEitherContentPattern(
    bookId, `%<h2>${term}</h2>%`, `%<h2>${term}, %`,
  )

  const results: MicropediaRow[] = []
  const seen = new Set<string>()
  for (const row of rows) {
    const match = row.content.match(/<h2>([^<]+)<\/h2>/)
    if (!match) continue
    const word = (match[1] ?? '').trim()
    // Guard the comma pattern: the head before the comma must equal the term.
    const headBeforeComma = word.split(',')[0]!.trim()
    if (headBeforeComma !== term && word !== term) continue
    if (seen.has(word)) continue
    seen.add(word)

    const text = await resolveMicropediaGloss(bookId, row.lineIndex)
    if (!text) continue

    results.push({ word, text, bookId, lineId: row.id, lineIndex: row.lineIndex })
  }

  return results
}

// ── ספר הערוך ─────────────────────────────────────────────────────────────────
//
// Structure: <b><big>HEADWORD</big></b> followed by definition on the same line.
// The headword is wrapped in both <b> and <big> tags.
// → exact match only: term must equal the extracted headword exactly.

function parseBigBoldLine(content: string): { word: string; text: string } | null {
  // Match <b><big>WORD</big></b> followed by the rest of the line.
  // The separator between </b> and the definition is \xa0 (non-breaking spaces),
  // so use [\s\xa0]* to handle both regular and non-breaking whitespace.
  const match = content.match(/<b><big>([^<]+)<\/big><\/b>[\s\u00a0]*(.+)$/)
  if (!match) return null
  const word = (match[1] ?? '').trim()
  const text = (match[2] ?? '').trim()
  if (!word || !text) return null
  return { word, text }
}

export async function aruchLookup(term: string): Promise<AruchRow[]> {
  // Use exact title match — '%ספר הערוך%' also matches 'הפלאה שבערכין על ספר הערוך'
  // which is a different book with no <big> entries.
  if (_aruchCache.ids === null) {
    const rows = await getBookIdByExactTitle('ספר הערוך')
    _aruchCache.ids = rows.map(r => r.id)
  }
  if (_aruchCache.ids.length === 0) return []
  const bookId = _aruchCache.ids[0]!

  // Pattern covers both with and without trailing space before </big>
  const rows = await getLinesWithEitherContentPattern(
    bookId, `%<big>${term}</big>%`, `%<big>${term} </big>%`,
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
