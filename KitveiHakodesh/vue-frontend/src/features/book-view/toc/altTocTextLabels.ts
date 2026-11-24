/**
 * Alt-TOC labels drawn in the book text, from EVERY alt structure of the book (not
 * only one "preferred" structure — the pane and the text are separate decisions).
 *
 * Per entry, BREAK (its own line above the line's text) or INLINE (before the text,
 * inside the running text in the no-line-break view):
 * - an entry with children (a parasha, a perek, "hilchot X") breaks;
 * - a leaf that is a NAME (a perek name, a parasha, "hilchot X") breaks too — many
 *   structures hold their section titles as leaves;
 * - a leaf that is a COUNTER ("סעיף ד", "דף ב.", "פסוק כג", "אות לח", "א", an aliyah
 *   "שני") is inline — only on a level of counters, since many names end in a
 *   numeral-shaped word ("נח", "המביא קמא", "כי תשא").
 * Two labels of the same type on one line are joined with " · ".
 *
 * Dropped:
 * - a whole structure whose book already prints its numbering at the start of its
 *   lines ("(ד)", "[א]"...) — decided per STRUCTURE from a sample, never per line:
 *   in a commentary on the Shulchan Aruch a line's "(ד)" is the seif-katan count and
 *   only by chance equals the seif letter;
 * - one entry whose label is already visible at the start of its own line or of the
 *   line just before or after it (TOC entries are often off by one line).
 */
import type { TocEntry } from '@/webview-host/queries.types'
import { lineIsWhollyBold } from '../lines/lineIsWhollyBold'

export interface AltTocLineLabels {
  breaks: string[]
  inlines: string[]
}

interface StructureEntries {
  structure: { id: number }
  entries: TocEntry[]
}

/** A level whose labels are at least this share counters is a counting level. */
const COUNTER_LEVEL_SHARE = 0.8

function isBreakEntry(entry: TocEntry, counterLevels: ReadonlySet<number>): boolean {
  if (entry.hasChildren) return true
  return !altTocCounterLabel(entry.text) || !counterLevels.has(entry.level)
}

/** The levels of one structure whose labels are mostly counters. */
function counterLevelsOf(entries: readonly TocEntry[]): Set<number> {
  const totals = new Map<number, [number, number]>()
  for (const e of entries) {
    const t = totals.get(e.level) ?? [0, 0]
    t[0] += altTocCounterLabel(e.text) ? 1 : 0
    t[1] += 1
    totals.set(e.level, t)
  }
  return new Set([...totals].filter(([, [c, n]]) => c / n >= COUNTER_LEVEL_SHARE).map(([level]) => level))
}

/** lineIndex → this line's labels, parents before children, structures in db order. */
export function buildAltTocLineLabels(
  sections: readonly StructureEntries[],
  structuresPrintingNumbers: ReadonlySet<number>,
): Map<number, AltTocLineLabels> {
  const map = new Map<number, AltTocLineLabels>()
  for (const section of sections) {
    if (structuresPrintingNumbers.has(section.structure.id)) continue
    const counterLevels = counterLevelsOf(section.entries)
    const entries = section.entries
      .filter((e) => e.lineIndex != null && e.text.trim() !== '')
      .sort((a, b) => a.level - b.level)
    for (const entry of entries) {
      let labels = map.get(entry.lineIndex!)
      if (labels == null) map.set(entry.lineIndex!, (labels = { breaks: [], inlines: [] }))
      const list = isBreakEntry(entry, counterLevels) ? labels.breaks : labels.inlines
      const text = entry.text.trim()
      if (!list.includes(text)) list.push(text)
    }
  }
  return map
}

// ── Where a break label is drawn ────────────────────────────────────────────

const HEADING_LINE = /^\s*<h([2-6])\b/i
/** How many heading lines directly above its line a break label climbs over. */
const MAX_HEADINGS_CLIMBED = 2

function headingRank(content: string | null | undefined): number | null {
  const m = HEADING_LINE.exec(content ?? '')
  return m ? Number(m[1]) : null
}

/**
 * The line a break label of line [source] is drawn above. A section title ("הלכות
 * חנוכה") is often anchored on the first line of text, just under the heading of its
 * first siman — but it is the larger division, so it goes above that heading. It never
 * climbs over a higher-rank heading ("אורח חיים" <h2> above "סימן א" <h3>).
 */
export function altTocBreakAnchor(source: number, contentAt: (index: number) => string | null | undefined): number {
  // A label on a heading line belongs to that heading: it stays right above it.
  if (headingRank(contentAt(source)) != null) return source
  let anchor = source
  let lowestRank: number | null = null
  while (source - anchor < MAX_HEADINGS_CLIMBED && anchor > 0) {
    const rank = headingRank(contentAt(anchor - 1))
    if (rank == null || (lowestRank != null && rank < lowestRank)) break
    lowestRank = rank
    anchor--
  }
  return anchor
}

/** The lines whose break labels are drawn above line [index] (its own and those climbing to it). */
export function altTocBreakSources(index: number, contentAt: (index: number) => string | null | undefined): number[] {
  const sources: number[] = []
  for (let source = index; source <= index + MAX_HEADINGS_CLIMBED; source++) {
    if (altTocBreakAnchor(source, contentAt) === index) sources.push(source)
  }
  return sources
}

// ── Already visible in the text ─────────────────────────────────────────────

const HTML_TAG = /<[^>]+>/g
const NIKUD = /[֑-ׇ]/g
const MATRES = /[וי]/g
/** Punctuation that closes a printed marker: "[אות יא]", "דף ב.", "סעיף א:". */
const MARKER_END = /[\])}:.,;\-–—]/g
const NON_LETTERS = /[^א-ת0-9 |]/g
const SPACES = /\s+/g
const HEADING_TAG = /^\s*<h\d\b/i

function normalizeForMatch(text: string, dropMatres: boolean, keepMarkerEnds = false): string {
  let t = text.replace(HTML_TAG, ' ').replace(NIKUD, '')
  if (dropMatres) t = t.replace(MATRES, '')
  t = keepMarkerEnds ? t.replace(MARKER_END, ' | ') : t.replace(/\|/g, ' ')
  return t.replace(NON_LETTERS, ' ').replace(SPACES, ' ').trim()
}

/** Short keys ("א", "ד") match half the lines by chance; numbering is judged per structure. */
const MIN_VISIBLE_KEY_LENGTH = 3
/** A few characters may precede the label at the line start, e.g. "(א)". */
const MAX_VISIBLE_OFFSET = 8

/** A bracketed marker opening a line: "[דף לא טור א]", "(סעיף ה' בהג"ה)", "[אות קיא] ב" - and the word after it. */
const LEADING_BRACKETED_MARKER = /^\s*[[(]([^\])]{1,40})[\])]\s*(\S*)/

/**
 * True when the line opens with a bracketed marker that holds the label: the marker
 * starts with it ("[דף לא טור א]" on "דף לא") or is it with the word after it
 * ("[אות קיא] ב" on "אות קיא ב").
 */
function opensBracketedMarker(head: string, normalizedLabel: string, dropMatres: boolean): boolean {
  const m = LEADING_BRACKETED_MARKER.exec(head.replace(HTML_TAG, '').replace(NIKUD, ''))
  if (m == null) return false
  const inner = normalizeForMatch(m[1]!, dropMatres)
  if (inner === normalizedLabel || inner.startsWith(normalizedLabel + ' ')) return true
  return normalizeForMatch(`${m[1]} ${m[2]}`, dropMatres) === normalizedLabel
}

function isVisibleIn(label: string, lines: readonly (string | null | undefined)[], dropMatres: boolean): boolean {
  const normalizedLabel = normalizeForMatch(label, dropMatres)
  if (normalizedLabel === '') return false
  const key = normalizedLabel.split(' ').slice(0, 3).join(' ')
  // Short keys are judged only against a line that is the label and nothing else.
  const shortKey = key.replace(/ /g, '').length < MIN_VISIBLE_KEY_LENGTH
  for (const line of lines) {
    if (line == null) continue
    const head = line.slice(0, 600)
    const normalizedLine = normalizeForMatch(head, dropMatres)
    if (normalizedLine === '') continue
    if (normalizedLine === normalizedLabel) return true
    if (shortKey) continue
    if (HEADING_TAG.test(head) || lineIsWhollyBold(head)) {
      // A title line: the label opening it, or it opening the label, is the same title.
      const index = normalizedLine.indexOf(key)
      if (index >= 0 && index <= MAX_VISIBLE_OFFSET) return true
      if (normalizedLine.length >= MIN_VISIBLE_KEY_LENGTH && normalizedLabel.startsWith(normalizedLine)) return true
    } else {
      // Running text: only a printed MARKER counts — the whole label, closed by
      // punctuation or the line end. A perek is named after its opening words
      // ("מאימתי קורין..."), which are no title.
      if (opensBracketedMarker(head, normalizedLabel, dropMatres)) return true
      const marked = normalizeForMatch(head, dropMatres, true)
      const index = marked.indexOf(normalizedLabel)
      if (index < 0 || index > MAX_VISIBLE_OFFSET) continue
      const after = marked.slice(index + normalizedLabel.length)
      if (after === '' || after.startsWith(' |')) return true
    }
  }
  return false
}

/**
 * True when [label] already shows at the start of one of [lines] (its own line and
 * its neighbours; null = not loaded or outside the book): as a title line (heading or
 * all-bold) that it opens or that opens it, or as a printed marker opening a line of
 * text. Compared as written and again without vav/yod — a label and the text often
 * differ in spelling (ביאור / באור), but dropping them always would erase numerals ("דף י").
 */
export function isAltTocLabelVisible(label: string, lines: readonly (string | null | undefined)[]): boolean {
  return isVisibleIn(label, lines, false) || isVisibleIn(label, lines, true)
}

// ── Numbering printed by the book itself ────────────────────────────────────

const HEBREW_LETTER_VALUES: Record<string, number> = {
  א: 1, ב: 2, ג: 3, ד: 4, ה: 5, ו: 6, ז: 7, ח: 8, ט: 9, י: 10, כ: 20, ך: 20, ל: 30, מ: 40, ם: 40,
  נ: 50, ן: 50, ס: 60, ע: 70, פ: 80, ף: 80, צ: 90, ץ: 90, ק: 100, ר: 200, ש: 300, ת: 400,
}

/**
 * A Hebrew numeral ("ד", "טו", "קצז", "תרע"): letters in descending value, at most one
 * tens letter and one units letter. A word ("ויצא", "תולדות") is not one.
 */
function isHebrewNumeral(word: string): boolean {
  if (!/^[א-ת]{1,4}$/.test(word)) return false
  const values = [...word].map((c) => HEBREW_LETTER_VALUES[c]!)
  for (let i = 1; i < values.length; i++) if (values[i]! > values[i - 1]!) return false
  // 15 and 16 are written ט+ו / ט+ז, never י+ה / י+ו.
  const units = word.endsWith('טו') || word.endsWith('טז') ? 1 : values.filter((v) => v < 10).length
  return values.filter((v) => v >= 10 && v < 100).length <= 1 && units <= 1
}

/** The counter of a label: its last word, without geresh/gershayim, when a numeral ("סעיף ד" → "ד"). */
export function altTocLabelNumber(label: string): string | null {
  const words = label.trim().split(/\s+/)
  const last = (words[words.length - 1] ?? '').replace(/["'״׳]/g, '')
  return isHebrewNumeral(last) ? last : null
}

/** The aliyot, counted like numerals. */
const ORDINALS = new Set(['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'ששי', 'שביעי', 'מפטיר'])

/** A counter label: up to three words ending in a numeral or an aliyah ordinal — "סעיף ד", "דף ב.", "(א)", "שני". */
export function altTocCounterLabel(label: string): boolean {
  const words = label.replace(/[[\]()]/g, ' ').trim().split(/\s+/)
  if (words.length === 0 || words.length > 3) return false
  const last = words[words.length - 1]!.replace(/["'״׳.:,]/g, '')
  return isHebrewNumeral(last) || ORDINALS.has(last)
}

/** True when [lineHtml] opens with [num] in a printed-numbering form: א / (א) / [א] / א. / א) / א: */
export function lineStartsWithNumber(lineHtml: string, num: string): boolean {
  const text = lineHtml.replace(HTML_TAG, '').replace(NIKUD, '').trimStart()
  const escaped = num.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return new RegExp(`^[\\[({]?${escaped}(?:[\\])}.:,]|\\s|$)`).test(text)
}

/**
 * A structure counts as numbered only when nearly all its leaves end in a numeral, and
 * enough of them: a parasha list whose one name happens to be numeral-shaped ("נח") is
 * not a numbering. Measured on the whole library (2026-11-11): the structures that
 * print their own numbering open 76-100% of their numbered lines with it, every other
 * numbered structure at most 41% (seif-katan counts that equal the seif by chance).
 */
const MIN_NUMBERED_LEAVES = 10
const MIN_NUMBERED_SHARE_OF_LEAVES = 0.8
const PRINTED_NUMBERING_SHARE = 0.6
/** Leaves per structure whose lines are read; checked against the whole library (2026-11-11). */
export const PRINTED_NUMBERING_SAMPLE_SIZE = 24

/**
 * The numbered leaves to read, spread evenly through the structure (deterministic), or
 * none when the structure is not a numbering at all.
 */
export function sampleNumberedLeaves(entries: readonly TocEntry[], size = PRINTED_NUMBERING_SAMPLE_SIZE): TocEntry[] {
  const leaves = entries.filter((e) => !e.hasChildren && e.lineId != null)
  const numbered = leaves.filter((e) => altTocLabelNumber(e.text) != null)
  if (numbered.length < MIN_NUMBERED_LEAVES || numbered.length < leaves.length * MIN_NUMBERED_SHARE_OF_LEAVES) return []
  if (numbered.length <= size) return numbered
  const step = numbered.length / size
  return Array.from({ length: size }, (_, i) => numbered[Math.floor(i * step)]!)
}

/** True when enough of the sampled leaves open their line with their own number. */
export function printsOwnNumbering(sample: readonly TocEntry[], contentByLineId: ReadonlyMap<number, string>): boolean {
  if (sample.length === 0) return false
  let matches = 0
  for (const entry of sample) {
    const content = contentByLineId.get(entry.lineId!)
    const num = altTocLabelNumber(entry.text)
    if (content != null && num != null && lineStartsWithNumber(content, num)) matches++
  }
  return matches / sample.length >= PRINTED_NUMBERING_SHARE
}
