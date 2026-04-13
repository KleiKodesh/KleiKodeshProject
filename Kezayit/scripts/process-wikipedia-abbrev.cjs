'use strict'
/**
 * process-wikipedia-abbrev.cjs
 *
 * Step 1: Parse old/rasheitevot.wikipedia.txt and write a clean
 *         old/rasheitevot.clean.txt with one entry per line:
 *         ABBREV=Hebrew expansion
 *
 * Step 2: Import the clean file into public/dictionary.db as source 'ויקיפדיה'.
 *
 * Entry format in the source file:
 *   "Hebrew expansion, ABBREV (transliteration) - English definition"
 *   OR
 *   "ABBREV, Hebrew expansion (transliteration) - English definition"
 *
 * The Hebrew expansion is already in Hebrew — no translation needed.
 *
 * Usage: node scripts/process-wikipedia-abbrev.cjs
 */

const Database = require('better-sqlite3')
const fs = require('fs')
const path = require('path')

const SRC = path.resolve(__dirname, '../old/rasheitevot.wikipedia.txt')
const CLEAN = path.resolve(__dirname, '../old/rasheitevot.clean.txt')
const DST_DB = path.resolve(__dirname, '../public/dictionary.db')
const WIKI_SOURCE_LABEL = 'ויקיפדיה'

const HEB = /[\u05D0-\u05EA]/
const GERESH = /[\u05F3\u05F4\u05F0-\u05F2״׳"']/

function normalizeAbbrev(s) {
  return s.trim()
    .replace(/\u05F4|״/g, '"')
    .replace(/\u05F3|׳/g, "'")
    .replace(/''/g, '"')
    .replace(/[\u05B0-\u05C7]/g, '') // strip nikud from abbreviation
    .trim()
}

function normalizeExpansion(s) {
  return s.trim()
    .replace(/\[([^\]]*)\]/g, '$1') // remove [ ] but keep content inside
    .replace(/[\[\]]/g, '')          // remove any remaining stray [ or ]
    .replace(/[\u05B0-\u05C7]/g, '') // strip nikud
    .replace(/\s+/g, ' ')
    .trim()
}

function isAbbrev(s) {
  return HEB.test(s) && GERESH.test(s) && s.length <= 20 && !s.includes('[') && !s.includes(']')
}

// ── Step 1: Parse and clean ───────────────────────────────────────────────────

const text = fs.readFileSync(SRC, 'utf8')
const lines = text.split('\n')

const entries = [] // { abbrev, expansion }
const skipped = []

for (const raw of lines) {
  const line = raw.trim()

  // Skip empty, non-Hebrew, section headers, [x] placeholders, bullet points
  if (!line || !HEB.test(line)) continue
  if (line.includes('[x]')) continue
  if (line.startsWith('=')) continue
  // Skip section dividers like "א", "א·א", "ב·ג" etc.
  if (/^[\u05D0-\u05EA·\s]{1,6}$/.test(line)) continue
  // Must have a comma and parenthesis to be an entry
  if (!line.includes(',') || !line.includes('(')) continue

  // Handle partially-abbreviated phrase entries that contain "[unabbrev] ABBREV]" pattern
  // e.g. "אישת חיל] עטרת בעלה, [א״ח] עט״ב] (..."
  // The abbreviation is the geresh token just before the last "]" before "("
  const bracketPhraseMatch = line.match(/\[([^\]]+)\]\s*([\u05D0-\u05EA\u05F3\u05F4\u05F0-\u05F2״׳"']+)\]/)
  if (bracketPhraseMatch && !line.includes('[x]')) {
    const abbrev = normalizeAbbrev(bracketPhraseMatch[2])
    const contextAbbrev = bracketPhraseMatch[1].trim() // e.g. "א״ח" — the unabbreviated context ref
    const beforeParen = line.split('(')[0]
    // Remove the abbreviation token, the context abbreviation ref, and normalize
    const withoutTokens = beforeParen
      .replace(bracketPhraseMatch[2], '') // remove abbrev token (e.g. עט״ב)
      .replace(contextAbbrev, '')          // remove context abbrev (e.g. א״ח)
      .replace(/,/g, ' ')
    const expansion = normalizeExpansion(withoutTokens)
    if (isAbbrev(abbrev) && HEB.test(expansion) && expansion.length >= 2) {
      entries.push({ abbrev, expansion })
    }
    continue
  }

  let processLine = line
  if (line.startsWith('[')) continue

  // Split at first "(" to get the part before transliteration
  const beforeParen = processLine.split('(')[0]
  const parts = beforeParen.split(',').map(s => s.trim()).filter(Boolean)
  if (parts.length < 2) continue

  // Try pattern 1: "Hebrew expansion, ABBREV"
  // Last part before "(" is the abbreviation
  const lastPart = normalizeAbbrev(parts[parts.length - 1])
  const firstParts = parts.slice(0, parts.length - 1).join(', ')
  const expansion1 = normalizeExpansion(firstParts)

  if (isAbbrev(lastPart) && HEB.test(expansion1) && expansion1.length >= 2) {
    entries.push({ abbrev: lastPart, expansion: expansion1 })
    continue
  }

  // Try pattern 2: "ABBREV, Hebrew expansion"
  const firstPart = normalizeAbbrev(parts[0])
  const restParts = parts.slice(1).join(', ')
  const expansion2 = normalizeExpansion(restParts)

  if (isAbbrev(firstPart) && HEB.test(expansion2) && expansion2.length >= 2) {
    entries.push({ abbrev: firstPart, expansion: expansion2 })
    continue
  }

  skipped.push(line.slice(0, 80))
}

// Deduplicate
const seen = new Set()
const unique = entries.filter(e => {
  const key = `${e.abbrev}|${e.expansion}`
  if (seen.has(key)) return false
  seen.add(key)
  return true
})

console.log(`Parsed: ${unique.length} entries (${entries.length - unique.length} duplicates removed)`)
console.log(`Skipped: ${skipped.length} lines`)

// Write clean file
const cleanLines = unique.map(e => `${e.abbrev}=${e.expansion}`)
fs.writeFileSync(CLEAN, cleanLines.join('\n'), 'utf8')
console.log(`\nWrote ${CLEAN}`)
console.log('\nSample entries:')
unique.slice(0, 20).forEach(e => console.log(`  [${e.abbrev}] → ${e.expansion}`))

// ── Step 2: Import into DB ────────────────────────────────────────────────────

const dst = new Database(DST_DB)
dst.pragma('journal_mode = WAL')
dst.pragma('foreign_keys = ON')

dst.prepare('INSERT OR IGNORE INTO source (label) VALUES (?)').run(WIKI_SOURCE_LABEL)
const sourceId = dst.prepare('SELECT id FROM source WHERE label = ?').get(WIKI_SOURCE_LABEL).id

// Build set of (headword, definition) pairs already in the DB
const existingPairs = new Set(
  dst.prepare(`
    SELECT s.headword, d.text
    FROM sense s JOIN definition d ON d.sense_id = s.id
    WHERE s.source_id != (SELECT id FROM source WHERE label = ?)
  `).all(WIKI_SOURCE_LABEL).map(r => `${r.headword}|${r.text}`)
)

const insertSense = dst.prepare(`
  INSERT OR IGNORE INTO sense (headword, nikud, etymology, cross_ref, source_id, sense_order)
  VALUES (?, NULL, NULL, NULL, ?, ?)
`)
const insertDef = dst.prepare(`
  INSERT OR IGNORE INTO definition (sense_id, text, def_order) VALUES (?, ?, 0)
`)
const getSenseId = dst.prepare(`
  SELECT id FROM sense WHERE headword = ? AND source_id = ? AND sense_order = ? LIMIT 1
`)
const getMaxSenseOrder = dst.prepare(`
  SELECT COALESCE(MAX(sense_order), -1) as max_order FROM sense WHERE headword = ? AND source_id = ?
`)

let inserted = 0, skippedDb = 0

dst.transaction(() => {
  for (const { abbrev, expansion } of unique) {
    // Skip only if this exact (abbrev, expansion) pair already exists
    if (existingPairs.has(`${abbrev}|${expansion}`)) { skippedDb++; continue }

    const nextOrder = getMaxSenseOrder.get(abbrev, sourceId).max_order + 1
    const r = insertSense.run(abbrev, sourceId, nextOrder)
    const senseId = r.changes > 0
      ? Number(r.lastInsertRowid)
      : getSenseId.get(abbrev, sourceId, nextOrder)?.id
    if (senseId) {
      const dr = insertDef.run(senseId, expansion)
      dr.changes > 0 ? inserted++ : skippedDb++
    }
  }
})()

const total = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
const sources = dst.prepare('SELECT label, COUNT(*) as c FROM sense s JOIN source src ON src.id=s.source_id GROUP BY src.id ORDER BY src.id').all()
dst.close()

console.log(`\nDB: Inserted ${inserted}, Skipped ${skippedDb}`)
console.log(`Total senses: ${total}`)
console.log('\nSources:')
sources.forEach(s => console.log(`  ${s.label}: ${s.c}`))
