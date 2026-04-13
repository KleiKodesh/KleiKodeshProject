'use strict'
/**
 * import-aramaic.cjs
 *
 * Imports Aramaic entries directly from ToratEmet's FinalDictionary.txt.
 * This is the authoritative source — 6,987 entries across 4 sources.
 *
 * FinalDictionary.txt format (one entry per line):
 *   <source_num> <headword>={nikud} definition text
 *   OR
 *   <source_num> <headword>=definition text   (no nikud)
 *
 * Source numbers:
 *   0 = מילון ארמי
 *   1 = מילון ארמי א
 *   2 = מילון ארמי ב
 *   3 = מילון ארמי ג
 *
 * Definition text may contain *** separators — each segment is a separate sense.
 * Each segment may start with {nikud} and/or (=etymology).
 *
 * Usage: node scripts/import-aramaic.cjs
 */

const Database = require('better-sqlite3')
const fs = require('fs')
const path = require('path')
const iconv = require('iconv-lite')

const SRC_FILE = 'C:\\Users\\Admin\\Documents\\ToratEmetInstall\\Dictionaries\\FinalDictionary.txt'
const DST_DB = path.resolve(__dirname, '../public/dictionary.db')

const SOURCE_LABELS = {
  0: 'מילון ארמי',
  1: 'מילון ארמי א',
  2: 'מילון ארמי ב',
  3: 'מילון ארמי ג',
}

// ── Segment parser ────────────────────────────────────────────────────────────

function parseSegment(segment, fallbackNikud) {
  let s = segment.trim()
  if (!s) return null

  // Extract {nikud} prefix
  let nikud = fallbackNikud ?? null
  const nikudMatch = s.match(/^\{([^}]+)\}\s*/)
  if (nikudMatch) {
    nikud = (nikudMatch[1] ?? '').trim() || null
    s = s.slice(nikudMatch[0].length).trim()
  }

  // Extract (=etymology) prefix
  let etymology = null
  const etymMatch = s.match(/^\(=([^)]+)\)\s*/)
  if (etymMatch) {
    etymology = (etymMatch[1] ?? '').trim() || null
    s = s.slice(etymMatch[0].length).trim()
  }

  if (!s) return null
  return { nikud, etymology, text: s }
}

function parseSenses(definition, headwordNikud) {
  return definition
    .split('***')
    .map((seg, i) => parseSegment(seg, i === 0 ? headwordNikud : null))
    .filter(Boolean)
}

// ── Read source file ──────────────────────────────────────────────────────────

// ToratEmet files are Windows-1255 (Hebrew Windows encoding)
let raw
try {
  raw = fs.readFileSync(SRC_FILE)
} catch (e) {
  // Fallback: try the old dist/dictionary.db approach
  console.error(`Cannot read ${SRC_FILE}: ${e.message}`)
  process.exit(1)
}

let text
try {
  // Try iconv-lite if available
  text = iconv.decode(raw, 'win1255')
} catch {
  // Fallback to Node's built-in latin1 + manual recode isn't reliable,
  // but try anyway
  text = raw.toString('binary')
}

const lines = text.split(/\r?\n/)
const dataLines = lines.filter((l) => /^[0-9] /.test(l))
console.log(`Read ${dataLines.length} entries from FinalDictionary.txt`)

// ── Import ────────────────────────────────────────────────────────────────────

const dst = new Database(DST_DB)
dst.pragma('journal_mode = WAL')
dst.pragma('foreign_keys = ON')

// Insert source rows
const insertSource = dst.prepare(`INSERT OR IGNORE INTO source (label) VALUES (?)`)
for (const label of Object.values(SOURCE_LABELS)) insertSource.run(label)

const sourceIdByLabel = new Map()
dst.prepare('SELECT id, label FROM source').all().forEach((r) => sourceIdByLabel.set(r.label, r.id))

const insertSense = dst.prepare(`
  INSERT OR IGNORE INTO sense (headword, nikud, etymology, source_id, sense_order)
  VALUES (?, ?, ?, ?, ?)
`)
const insertDef = dst.prepare(`
  INSERT OR IGNORE INTO definition (sense_id, text, def_order)
  VALUES (?, ?, 0)
`)
const getSenseId = dst.prepare(`
  SELECT id FROM sense WHERE headword = ? AND source_id = ? AND sense_order = ? LIMIT 1
`)

let totalSenses = 0
let multiSenseEntries = 0

const importAll = dst.transaction(() => {
  for (const line of dataLines) {
    // Parse: "N headword={nikud} definition" or "N headword=definition"
    const m = line.match(/^(\d) (.+?)=(.+)$/)
    if (!m) continue

    const sourceNum = parseInt(m[1], 10)
    // Normalize '' (double single-quote) → " (geresh) in headwords
    const headword = (m[2] ?? '').trim().replace(/''/g, '"')
    const rawDef = (m[3] ?? '').trim()

    // Skip entries with no real definition (just * placeholder)
    if (rawDef === '*') continue

    const label = SOURCE_LABELS[sourceNum] ?? 'מילון ארמי'
    const sourceId = sourceIdByLabel.get(label)
    if (!sourceId) continue

    // The definition may start with {nikud} before the *** split
    // Extract top-level nikud first (applies to first segment as fallback)
    let topNikud = null
    let defText = rawDef.replace(/''/g, '"')
    const topNikudMatch = rawDef.match(/^\{([^}]+)\}\s*/)
    if (topNikudMatch) {
      topNikud = (topNikudMatch[1] ?? '').trim() || null
      defText = rawDef.slice(topNikudMatch[0].length).trim()
    }

    const senses = parseSenses(defText, topNikud)
    if (!senses.length) continue

    if (senses.length > 1) multiSenseEntries++

    senses.forEach((sense, order) => {
      const senseResult = insertSense.run(headword, sense.nikud, sense.etymology, sourceId, order)
      const senseId = senseResult.changes > 0
        ? Number(senseResult.lastInsertRowid)
        : getSenseId.get(headword, sourceId, order)?.id

      if (senseId) {
        insertDef.run(senseId, sense.text)
        totalSenses++
      }
    })
  }
})

importAll()

const senseCount = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
const defCount = dst.prepare('SELECT COUNT(*) as c FROM definition').get().c
dst.close()

console.log(`Done.`)
console.log(`  Entries processed: ${dataLines.length}`)
console.log(`  Multi-sense entries (had ***): ${multiSenseEntries}`)
console.log(`  Total senses inserted: ${totalSenses}`)
console.log(`  DB sense rows: ${senseCount}, definition rows: ${defCount}`)
