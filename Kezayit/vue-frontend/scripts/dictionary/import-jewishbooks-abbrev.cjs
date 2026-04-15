'use strict'
/**
 * import-jewishbooks-abbrev.cjs
 *
 * Imports the Hebrew abbreviations table from wiki.jewishbooks.org.il into
 * public/dictionary.db as a new source 'ויקי ספרי יהדות - ראשי תיבות'.
 *
 * Source page: https://wiki.jewishbooks.org.il/mediawiki/wiki/ראשי_תיבות_וקיצורים
 * License: CC BY-SA (MediaWiki wiki)
 *
 * Table format per row:
 *   |'''abbrev'''||expansion1{{ש}}expansion2||extra1||extra2||...||notes
 *
 * Each expansion becomes a separate sense row (one headword can have many expansions).
 * Source footnotes ({{הערה|...}}) are stripped — they reference the original books.
 *
 * Usage: node scripts/dictionary/import-jewishbooks-abbrev.cjs
 */

const https    = require('https')
const Database = require('better-sqlite3')
const path     = require('path')

const DST_DB   = path.resolve(__dirname, '../../public/dicts/kezayit_dictionary.db')
const API      = 'https://wiki.jewishbooks.org.il/mediawiki/api.php'
const PAGE     = 'ראשי_תיבות_וקיצורים'
const SOURCE_LABEL = 'ויקי ספרי יהדות - ראשי תיבות'
const SOURCE_LANG  = 'ראשי תיבות'
const SOURCE_URL   = 'https://wiki.jewishbooks.org.il/mediawiki/wiki/%D7%A8%D7%90%D7%A9%D7%99_%D7%AA%D7%99%D7%91%D7%95%D7%AA_%D7%95%D7%A7%D7%99%D7%A6%D7%95%D7%A8%D7%99%D7%9D'

// ── HTTP ──────────────────────────────────────────────────────────────────────

function get(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { 'User-Agent': 'KezayitDictBot/1.0' } }, res => {
      let d = ''; res.on('data', c => d += c); res.on('end', () => resolve(d))
    }).on('error', reject)
  })
}

// ── Wikitext helpers ──────────────────────────────────────────────────────────

function stripTemplates(s) {
  // Remove {{הערה|...}} footnotes (may be nested)
  let result = s
  let prev
  do {
    prev = result
    result = result.replace(/\{\{[^{}]*\}\}/g, '')
  } while (result !== prev)
  return result
}

function cleanCell(s) {
  return stripTemplates(s)
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')  // [[link|text]] → text
    .replace(/\[https?:[^\]]+\]/g, '')                 // external links
    .replace(/'{2,3}/g, '')                            // bold/italic
    .replace(/<[^>]+>/g, '')                           // HTML tags
    .replace(/\s+/g, ' ')
    .trim()
}

// Split a cell on {{ש}} (line break template) to get multiple expansions
function splitExpansions(cell) {
  return cell
    .split(/\{\{ש\}\}/gi)
    .map(s => cleanCell(s))
    .filter(s => s && s !== '--' && s.length > 0)
}

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

// ── Parse wikitext table ──────────────────────────────────────────────────────

function parseTable(wikitext) {
  const entries = []
  const lines = wikitext.split('\n')

  for (const line of lines) {
    // Table data rows start with | and have bold abbrev: |'''abbrev'''||...
    if (!line.match(/^\|\s*'''/)) continue

    // Split on || to get cells
    const cells = line.split('||')
    if (cells.length < 2) continue

    // First cell: the abbreviation
    const abbrevRaw = cleanCell((cells[0] || '').replace(/^\|/, '').trim())
    if (!abbrevRaw || !containsHebrew(abbrevRaw)) continue

    // Remaining cells: expansions (columns 2-7) + notes (last column)
    // Collect all non-empty, non-dash expansions from all expansion columns
    const expansions = []
    for (let i = 1; i < cells.length - 1; i++) {
      const cell = cells[i] || ''
      const parts = splitExpansions(cell)
      for (const p of parts) {
        if (p && p !== '--' && containsHebrew(p) && !expansions.includes(p)) {
          expansions.push(p)
        }
      }
    }

    if (expansions.length === 0) continue

    entries.push({ abbrev: abbrevRaw, expansions })
  }

  return entries
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  // 1. Fetch wikitext
  console.log('Fetching wikitext...')
  const url = `${API}?action=query&titles=${encodeURIComponent(PAGE)}&prop=revisions&rvprop=content&rvslots=main&format=json`
  const raw = await get(url)
  const data = JSON.parse(raw)
  const wikitext = Object.values(data?.query?.pages ?? {})[0]?.revisions?.[0]?.slots?.main?.['*'] ?? ''
  if (!wikitext) { console.error('No wikitext found'); process.exit(1) }
  console.log(`Wikitext length: ${wikitext.length} chars`)

  // 2. Parse
  const entries = parseTable(wikitext)
  console.log(`Parsed ${entries.length} abbreviation entries`)

  // 3. Import into dictionary.db
  const db = new Database(DST_DB)
  db.pragma('journal_mode = WAL')
  db.pragma('foreign_keys = ON')

  // Insert or get source
  db.prepare(`INSERT OR IGNORE INTO source (label, lang, url) VALUES (?, ?, ?)`).run(SOURCE_LABEL, SOURCE_LANG, SOURCE_URL)
  const sourceId = db.prepare(`SELECT id FROM source WHERE label = ?`).get(SOURCE_LABEL).id
  console.log(`Source id: ${sourceId} (${SOURCE_LABEL})`)

  // Remove existing entries for this source (idempotent)
  const existing = db.prepare(`SELECT COUNT(*) as c FROM sense WHERE source_id = ?`).get(sourceId).c
  if (existing > 0) {
    db.prepare(`DELETE FROM definition WHERE sense_id IN (SELECT id FROM sense WHERE source_id = ?)`).run(sourceId)
    db.prepare(`DELETE FROM sense WHERE source_id = ?`).run(sourceId)
    console.log(`Removed ${existing} existing senses for this source`)
  }

  const insertSense = db.prepare(`
    INSERT OR IGNORE INTO sense (headword, nikud, pos, binyan, shoresh, ktiv_male, etymology, cross_ref, period_tag, source_id, sense_order)
    VALUES (?, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ?, ?)
  `)
  const insertDef = db.prepare(`
    INSERT OR IGNORE INTO definition (sense_id, text, filter_tag, def_order)
    VALUES (?, ?, NULL, 0)
  `)
  const getSenseId = db.prepare(`SELECT id FROM sense WHERE headword = ? AND source_id = ? AND sense_order = ? LIMIT 1`)

  let totalSenses = 0
  let totalDefs = 0

  db.transaction(() => {
    for (const { abbrev, expansions } of entries) {
      expansions.forEach((expansion, order) => {
        const r = insertSense.run(abbrev, sourceId, order)
        const senseId = r.changes > 0
          ? Number(r.lastInsertRowid)
          : getSenseId.get(abbrev, sourceId, order)?.id
        if (!senseId) return
        insertDef.run(senseId, expansion)
        totalSenses++
        totalDefs++
      })
    }
  })()

  const senseCount = db.prepare(`SELECT COUNT(*) as c FROM sense WHERE source_id = ?`).get(sourceId).c
  db.close()

  console.log(`\nDone!`)
  console.log(`  Entries: ${entries.length}`)
  console.log(`  Senses inserted: ${totalSenses}`)
  console.log(`  Definitions inserted: ${totalDefs}`)
  console.log(`  DB senses for this source: ${senseCount}`)
}

main().catch(e => { console.error('Fatal:', e.message); process.exit(1) })
