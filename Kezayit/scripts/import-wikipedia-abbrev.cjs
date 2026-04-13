'use strict'
/**
 * import-wikipedia-abbrev.cjs
 *
 * Imports Hebrew abbreviations from old/rasheitevot.wikipedia.txt into
 * public/dictionary.db as source 'ויקיפדיה'.
 *
 * File format (two patterns):
 *   1. "Hebrew expansion, אב״ד (transliteration) - English definition"
 *      → headword = אב״ד, definition = Hebrew expansion
 *   2. "אג״ק, אִגָּרוֹת קֹדֶשׁ (transliteration) - English definition"
 *      → headword = אג״ק, definition = אִגָּרוֹת קֹדֶשׁ
 *
 * Usage: node scripts/import-wikipedia-abbrev.cjs
 */

const Database = require('better-sqlite3')
const fs = require('fs')
const path = require('path')

const SRC = path.resolve(__dirname, '../old/rasheitevot.wikipedia.txt')
const DST_DB = path.resolve(__dirname, '../public/dictionary.db')
const WIKI_SOURCE_LABEL = 'ויקיפדיה'

// ── Parser ────────────────────────────────────────────────────────────────────

const HEB = /[\u05D0-\u05EA]/
const GERESH = /[\u05F3\u05F4״׳"']/

function normalizeAbbrev(s) {
  return s.trim()
    .replace(/\u05F4|״/g, '"')
    .replace(/\u05F3|׳/g, "'")
    .replace(/''/g, '"')
}

function isAbbrev(s) {
  return HEB.test(s) && GERESH.test(s)
}

function parseEntries(text) {
  const entries = []
  const lines = text.split('\n')

  for (const raw of lines) {
    const line = raw.trim()
    if (!line || !HEB.test(line)) continue
    // Skip section headers, intro text, bullet points, single-letter section markers
    if (line.startsWith('[') || line.startsWith('=')) continue
    // Skip lines that are just a single Hebrew letter (section dividers like "א", "ב·א")
    if (/^[\u05D0-\u05EA][\u05D0-\u05EA·\s]*$/.test(line)) continue
    // Skip lines without a comma (not an entry line)
    if (!line.includes(',')) continue
    // Skip gematria placeholder entries like ״ט[x] or ״פ[x]
    if (line.includes('[x]')) continue

    // Pattern 1: "Hebrew expansion, ABBREV (transliteration) - ..."
    // The abbreviation is the token right before the first "("
    // and it contains geresh chars
    const beforeParen = line.split('(')[0]
    if (!beforeParen) continue

    const parts = beforeParen.split(',').map(s => s.trim()).filter(Boolean)
    if (parts.length < 2) continue

    // Last part before "(" is the abbreviation candidate
    const abbrevCandidate = normalizeAbbrev(parts[parts.length - 1])
    // First part(s) are the Hebrew expansion
    const expansionCandidate = parts.slice(0, parts.length - 1).join(', ').trim()

    if (isAbbrev(abbrevCandidate) && HEB.test(expansionCandidate)) {
      // Clean expansion: strip nikud for the definition text
      const definition = expansionCandidate
        .replace(/[\u05B0-\u05C7]/g, '') // strip nikud
        .replace(/\s+/g, ' ')
        .trim()
      if (definition.length >= 2) {
        entries.push({ abbrev: abbrevCandidate, definition })
      }
      continue
    }

    // Pattern 2: "ABBREV, Hebrew expansion (transliteration) - ..."
    // First token is the abbreviation
    const firstPart = normalizeAbbrev(parts[0])
    const secondPart = parts.slice(1).join(', ').trim()

    if (isAbbrev(firstPart) && HEB.test(secondPart)) {
      const definition = secondPart
        .replace(/[\u05B0-\u05C7]/g, '')
        .replace(/\s+/g, ' ')
        .trim()
      if (definition.length >= 2) {
        entries.push({ abbrev: firstPart, definition })
      }
    }
  }

  // Deduplicate by abbrev+definition
  const seen = new Set()
  return entries.filter(e => {
    const key = `${e.abbrev}|${e.definition}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

// ── Import ────────────────────────────────────────────────────────────────────

const text = fs.readFileSync(SRC, 'utf8')
const entries = parseEntries(text)

console.log(`Parsed ${entries.length} entries`)
console.log('\nSample:')
entries.slice(0, 15).forEach(e => console.log(`  [${e.abbrev}] → ${e.definition}`))

const dst = new Database(DST_DB)
dst.pragma('journal_mode = WAL')
dst.pragma('foreign_keys = ON')

dst.prepare('INSERT OR IGNORE INTO source (label) VALUES (?)').run(WIKI_SOURCE_LABEL)
const sourceId = dst.prepare('SELECT id FROM source WHERE label = ?').get(WIKI_SOURCE_LABEL).id

const insertSense = dst.prepare(`
  INSERT OR IGNORE INTO sense (headword, nikud, etymology, cross_ref, source_id, sense_order)
  VALUES (?, NULL, NULL, NULL, ?, 0)
`)
const insertDef = dst.prepare(`
  INSERT OR IGNORE INTO definition (sense_id, text, def_order) VALUES (?, ?, 0)
`)
const getSenseId = dst.prepare(`
  SELECT id FROM sense WHERE headword = ? AND source_id = ? AND sense_order = 0 LIMIT 1
`)

let inserted = 0, skipped = 0

dst.transaction(() => {
  for (const { abbrev, definition } of entries) {
    const r = insertSense.run(abbrev, sourceId)
    const senseId = r.changes > 0
      ? Number(r.lastInsertRowid)
      : getSenseId.get(abbrev, sourceId)?.id
    if (senseId) {
      const dr = insertDef.run(senseId, definition)
      dr.changes > 0 ? inserted++ : skipped++
    }
  }
})()

const total = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
dst.close()

console.log(`\nInserted: ${inserted}, Skipped: ${skipped}`)
console.log(`Total senses in DB: ${total}`)
