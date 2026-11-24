// Seforim DB queries for the dictionary feature.
// Queries מצודת ציון, מלבי"ם באור המילות, מחברת מנחם, מיקרופדיה, and ספר הערוך from the main seforim DB.
// Headwords come from the seforim_headword index in the dictionary DB (dictSeforimHeadwords):
// pointers of (book title, lineIndex). Titles resolve to seforim.db ids at runtime (cached),
// and only the pointed-to lines are fetched - no LIKE over seforim.db content, which matches
// nothing on a zstd-compressed library.

import { getBookIdByExactTitle, getLineByBookAndLineIndex } from './seforimApi'
import { serviceCall } from './serviceClient'

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

// ── Headword pointers -> seforim.db lines ─────────────────────────────────────

type HeadwordSource = 'metzudat' | 'malbim' | 'menachem' | 'aruch' | 'micropedia'
type HeadwordMode = 'exact' | 'prefix' | 'contains'

interface HeadwordPointer {
  source:    string
  bookTitle: string
  lineIndex: number
  word:      string
}

interface PointedLine {
  bookTitle: string
  bookId:    number
  lineId:    number
  lineIndex: number
  content:   string
}

async function findHeadwords(source: HeadwordSource, term: string, mode: HeadwordMode): Promise<HeadwordPointer[]> {
  if (!term) return []
  return (await serviceCall<{ rows: HeadwordPointer[] }>(
    'dictSeforimHeadwords', { source, term, mode })).rows
}

/** Title -> seforim.db book id (null = not in this library). Ids differ between libraries; titles do not. */
const _bookIdByTitle = new Map<string, Promise<number | null>>()

function resolveBookId(title: string): Promise<number | null> {
  let pending = _bookIdByTitle.get(title)
  if (!pending) {
    pending = getBookIdByExactTitle(title)
      .then(rows => rows[0]?.id ?? null)
      .catch(() => { _bookIdByTitle.delete(title); return null })
    _bookIdByTitle.set(title, pending)
  }
  return pending
}

/** Fetches each pointed-to line once (several word tokens can point at one line), in pointer order. */
async function fetchPointedLines(pointers: HeadwordPointer[]): Promise<PointedLine[]> {
  const seen = new Set<string>()
  const unique = pointers.filter(p => {
    const key = `${p.bookTitle}::${p.lineIndex}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
  const lines = await Promise.all(unique.map(async (p): Promise<PointedLine | null> => {
    const bookId = await resolveBookId(p.bookTitle)
    if (bookId === null) return null
    const line = (await getLineByBookAndLineIndex(bookId, p.lineIndex))[0]
    if (!line) return null
    return { bookTitle: p.bookTitle, bookId, lineId: line.id, lineIndex: line.lineIndex, content: line.content }
  }))
  return lines.filter((l): l is PointedLine => l !== null)
}

async function lookupLines(source: HeadwordSource, term: string, mode: HeadwordMode): Promise<PointedLine[]> {
  return fetchPointedLines(await findHeadwords(source, term, mode))
}

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

/** The two bold-headword sources; callers name one instead of passing book ids. */
export type BoldSource = 'metzudat' | 'malbim'

async function queryBoldLines(source: BoldSource, term: string, mode: HeadwordMode): Promise<MetzudatRow[]> {
  return (await lookupLines(source, term, mode))
    .map(r => parseBoldLine(r.content, r.bookTitle, r.bookId, r.lineId, r.lineIndex))
    .filter((r): r is MetzudatRow => r !== null)
}

export async function boldExact(term: string, source: BoldSource): Promise<MetzudatRow[]> {
  const seen = new Set<string>()
  return (await queryBoldLines(source, term, 'exact')).filter(r => {
    if (!headerMatchesExact(r.word, term)) return false
    const key = `${r.bookId}::${r.word}::${r.definition}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export async function boldPrefix(term: string, source: BoldSource): Promise<MetzudatRow[]> {
  return (await queryBoldLines(source, term, 'prefix'))
    .filter(r => headerMatchesPrefix(r.word, term))
}

export async function boldContains(term: string, source: BoldSource): Promise<MetzudatRow[]> {
  return (await queryBoldLines(source, term, 'contains'))
    .filter(r => {
      const normalized = normalizeHeaderWord(r.word)
      return normalized.split(/[,\s]+/).some(
        token => token.includes(term) && !token.startsWith(term)
      )
    })
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
  // Only the dictionary section (<strong><big>HEADWORD</big></strong> lines) is indexed;
  // the early synonym/intro preamble is not dictionary content.
  const bigRows = await lookupLines('menachem', term, 'exact')

  const results: MenchemRow[] = []
  for (const row of bigRows) {
    const word = parseBigWord(row.content)
    if (!word) continue
    const normalized = word.replace(/[.,;:״"]/g, '').trim()
    if (!normalized.includes(term)) continue

    const nextRows = await getLineByBookAndLineIndex(row.bookId, row.lineIndex + 1)
    const nextLine = nextRows[0]
    if (!nextLine) continue
    results.push({
      word,
      text:      stripAllHtml(nextLine.content),
      title:     null,
      bookId:    row.bookId,
      lineId:    row.lineId,
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
  // The index holds every comma/space token of each <h2> headword, so TERM also finds
  // comma-qualified entries ('TERM, ...'); the guard below keeps only those headed by TERM.
  const rows = await lookupLines('micropedia', term, 'exact')

  const results: MicropediaRow[] = []
  const seen = new Set<string>()
  for (const row of rows) {
    const match = row.content.match(/<h2>([^<]+)<\/h2>/)
    if (!match) continue
    const word = (match[1] ?? '').trim()
    const headBeforeComma = word.split(',')[0]!.trim()
    if (headBeforeComma !== term && word !== term) continue
    if (seen.has(word)) continue
    seen.add(word)

    const text = await resolveMicropediaGloss(row.bookId, row.lineIndex)
    if (!text) continue

    results.push({ word, text, bookId: row.bookId, lineId: row.lineId, lineIndex: row.lineIndex })
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
  const rows = await lookupLines('aruch', term, 'exact')

  const results: AruchRow[] = []
  for (const row of rows) {
    const parsed = parseBigBoldLine(row.content)
    if (!parsed) continue
    const normalized = parsed.word.replace(/[.,;:״"]/g, '').trim()
    if (!normalized.includes(term)) continue

    results.push({
      word:      parsed.word,
      text:      stripAllHtml(parsed.text),
      bookId:    row.bookId,
      lineId:    row.lineId,
      lineIndex: row.lineIndex,
    })
  }

  return results
}
