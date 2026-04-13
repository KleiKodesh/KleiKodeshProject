'use strict'
/**
 * import-aramaic.cjs
 *
 * Imports Aramaic entries from FinalDictionary.txt into public/dictionary.db.
 *
 * For entries with definition "*" (no direct definition), resolves them at
 * build time using a suffix/prefix abbreviation algorithm:
 *   1. Exact match in the abbreviation map
 *   2. Strip Hebrew preposition prefix (ב,ל,כ,מ,ו,ש,ה,ד,...), exact match
 *   3. Find geresh position, try all suffix splits
 *   4. Strip prefix + suffix splits
 *
 * Resolved entries get a cross_ref column pointing to the abbreviation used.
 * Unresolved entries are skipped (they are gematria numbers / letter names).
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
  0: 'תורת אמת - מילון ארמי',
  1: 'תורת אמת - מילון ארמי א',
  2: 'תורת אמת - מילון ארמי ב',
  3: 'תורת אמת - מילון ארמי ג',
}

const PREFIXES = [
  'מב', 'מל', 'וב', 'ול', 'שב', 'של', 'הב', 'כב', 'כל', 'דב', 'דל',
  'ב', 'ל', 'כ', 'מ', 'ו', 'ש', 'ה', 'ד',
]

// ── Segment parser ────────────────────────────────────────────────────────────

function parseSegment(segment, fallbackNikud) {
  let s = segment.trim()
  if (!s) return null

  let nikud = fallbackNikud ?? null
  const nikudMatch = s.match(/^\{([^}]+)\}\s*/)
  if (nikudMatch) {
    nikud = (nikudMatch[1] ?? '').trim() || null
    s = s.slice(nikudMatch[0].length).trim()
  }

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

// ── Abbreviation resolver ─────────────────────────────────────────────────────

function buildAbbrevMap(normalEntries) {
  const map = new Map()
  for (const line of normalEntries) {
    const m = line.match(/^[0-9] (.+?)=(.+)$/)
    if (!m) continue
    const hw = m[1].trim().replace(/''/g, '"')
    const def = m[2].trim()
    if (def === '*') continue
    if (!map.has(hw)) map.set(hw, [])
    map.get(hw).push(def)
  }
  return map
}

function resolveAbbrev(hw, abbrevMap) {
  // 1. Exact
  if (abbrevMap.has(hw)) return { prefix: '', abbrev: hw, expansions: abbrevMap.get(hw) }

  // 2. Strip prefix, exact match
  for (const pfx of PREFIXES) {
    if (hw.startsWith(pfx) && hw.length > pfx.length) {
      const rest = hw.slice(pfx.length)
      if (abbrevMap.has(rest)) return { prefix: pfx, abbrev: rest, expansions: abbrevMap.get(rest) }
    }
  }

  // 3. Find geresh position, try suffix splits
  const gereshPos = hw.indexOf('"')
  if (gereshPos > 0) {
    for (let i = Math.max(0, gereshPos - 1); i >= 0; i--) {
      const suffix = hw.slice(i)
      if (abbrevMap.has(suffix)) {
        return { prefix: hw.slice(0, i), abbrev: suffix, expansions: abbrevMap.get(suffix) }
      }
    }
  }

  // 4. Strip prefix + geresh suffix splits
  for (const pfx of PREFIXES) {
    if (!hw.startsWith(pfx)) continue
    const rest = hw.slice(pfx.length)
    const gp = rest.indexOf('"')
    if (gp > 0) {
      for (let i = Math.max(0, gp - 1); i >= 0; i--) {
        const suffix = rest.slice(i)
        if (abbrevMap.has(suffix)) {
          return { prefix: pfx + rest.slice(0, i), abbrev: suffix, expansions: abbrevMap.get(suffix) }
        }
      }
    }
  }

  return null
}

// ── Read source file ──────────────────────────────────────────────────────────

const raw = fs.readFileSync(SRC_FILE)
const text = iconv.decode(raw, 'win1255')
const allLines = text.split(/\r?\n/).filter(l => /^[0-9] /.test(l))

const starLines = allLines.filter(l => /^[0-9] .+=\*$/.test(l))
const normalLines = allLines.filter(l => !/^[0-9] .+=\*$/.test(l))

const abbrevMap = buildAbbrevMap(normalLines)

console.log(`Total entries: ${allLines.length}`)
console.log(`Normal entries: ${normalLines.length}`)
console.log(`Star (*) entries: ${starLines.length}`)

// ── Import ────────────────────────────────────────────────────────────────────

const dst = new Database(DST_DB)
dst.pragma('journal_mode = WAL')
dst.pragma('foreign_keys = ON')

// Add cross_ref column to sense if not present
const cols = dst.prepare('PRAGMA table_info(sense)').all().map(c => c.name)
if (!cols.includes('cross_ref')) {
  dst.exec('ALTER TABLE sense ADD COLUMN cross_ref TEXT')
  console.log('Added cross_ref column to sense')
}

// Insert source rows
const insertSource = dst.prepare('INSERT OR IGNORE INTO source (label) VALUES (?)')
for (const label of Object.values(SOURCE_LABELS)) insertSource.run(label)

const sourceIdByLabel = new Map()
dst.prepare('SELECT id, label FROM source').all().forEach(r => sourceIdByLabel.set(r.label, r.id))

const insertSense = dst.prepare(`
  INSERT OR IGNORE INTO sense (headword, nikud, etymology, cross_ref, source_id, sense_order)
  VALUES (?, ?, ?, ?, ?, ?)
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
let starResolved = 0
let starSkipped = 0

const importAll = dst.transaction(() => {
  // ── Normal entries ──────────────────────────────────────────────────────────
  for (const line of normalLines) {
    const m = line.match(/^(\d) (.+?)=(.+)$/)
    if (!m) continue

    const sourceNum = parseInt(m[1], 10)
    const headword = m[2].trim().replace(/''/g, '"')
    const rawDef = m[3].trim().replace(/''/g, '"')

    if (rawDef === '*') continue

    const label = SOURCE_LABELS[sourceNum] ?? 'מילון ארמי'
    const sourceId = sourceIdByLabel.get(label)
    if (!sourceId) continue

    let topNikud = null
    let defText = rawDef
    const topNikudMatch = rawDef.match(/^\{([^}]+)\}\s*/)
    if (topNikudMatch) {
      topNikud = (topNikudMatch[1] ?? '').trim() || null
      defText = rawDef.slice(topNikudMatch[0].length).trim()
    }

    const senses = parseSenses(defText, topNikud)
    if (!senses.length) continue
    if (senses.length > 1) multiSenseEntries++

    senses.forEach((sense, order) => {
      const result = insertSense.run(headword, sense.nikud, sense.etymology, null, sourceId, order)
      const senseId = result.changes > 0
        ? Number(result.lastInsertRowid)
        : getSenseId.get(headword, sourceId, order)?.id
      if (senseId) { insertDef.run(senseId, sense.text); totalSenses++ }
    })
  }

  // ── Star entries — resolve at build time ────────────────────────────────────
  for (const line of starLines) {
    const m = line.match(/^(\d) (.+?)=\*$/)
    if (!m) continue

    const sourceNum = parseInt(m[1], 10)
    const headword = m[2].trim().replace(/''/g, '"')
    const label = SOURCE_LABELS[sourceNum] ?? 'מילון ארמי'
    const sourceId = sourceIdByLabel.get(label)
    if (!sourceId) continue

    const resolved = resolveAbbrev(headword, abbrevMap)
    if (!resolved) { starSkipped++; continue }

    // Build definition from all expansions
    const defText = resolved.expansions
      .map(e => {
        // Strip {nikud} wrappers from the expansion text for clean display
        return e.replace(/^\{[^}]+\}\s*/, '').trim()
      })
      .filter(Boolean)
      .join(', ')

    if (!defText) { starSkipped++; continue }

    // cross_ref = the abbreviation that was matched (e.g. 'ר"מ' for 'דלר"מ')
    const crossRef = resolved.abbrev

    const result = insertSense.run(headword, null, null, crossRef, sourceId, 0)
    const senseId = result.changes > 0
      ? Number(result.lastInsertRowid)
      : getSenseId.get(headword, sourceId, 0)?.id
    if (senseId) { insertDef.run(senseId, defText); totalSenses++; starResolved++ }
  }
})

importAll()

const senseCount = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
const defCount = dst.prepare('SELECT COUNT(*) as c FROM definition').get().c
const crossRefCount = dst.prepare("SELECT COUNT(*) as c FROM sense WHERE cross_ref IS NOT NULL").get().c
dst.close()

console.log(`\nDone.`)
console.log(`  Normal senses inserted: ${totalSenses - starResolved}`)
console.log(`  Star entries resolved:  ${starResolved}`)
console.log(`  Star entries skipped (gematria/letter names): ${starSkipped}`)
console.log(`  Multi-sense entries (***): ${multiSenseEntries}`)
console.log(`  DB sense rows: ${senseCount}, definition rows: ${defCount}`)
console.log(`  Cross-reference senses: ${crossRefCount}`)
