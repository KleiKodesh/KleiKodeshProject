'use strict'
/**
 * import-wiktionary.cjs
 *
 * Downloads the kaikki.org Hebrew Wiktionary JSONL dump and imports it into
 * public/wikidictionary.db.
 *
 * The kaikki dump is pre-parsed structured JSON — no wikitext parsing needed.
 * Each line is one entry with senses, definitions, examples, categories, etc.
 *
 * filter_tag: the raw "tags" array from each sense/definition is stored as a
 * comma-separated string in definition.filter_tag so future filtering can be
 * applied without re-importing. NULL means no tags (untagged = always shown).
 *
 * Usage:
 *   node scripts/import-wiktionary.cjs
 *
 * Options (env vars):
 *   DUMP_PATH   — path to a local .jsonl file (skips download)
 *   DUMP_URL    — override the download URL
 *   NO_DOWNLOAD — set to '1' to skip download even if file is missing (for testing)
 */

const Database = require('better-sqlite3')
const fs = require('fs')
const path = require('path')
const https = require('https')
const zlib = require('zlib')

const DST_DB = path.resolve(__dirname, '../public/wikidictionary.db')
const DUMP_DIR = path.resolve(__dirname, '../data')
const DUMP_FILE = path.join(DUMP_DIR, 'kaikki-hewiktionary.jsonl')

// kaikki.org Hebrew Wiktionary dump (Hebrew words defined in Hebrew Wiktionary)
const DUMP_URL =
  process.env.DUMP_URL ||
  'https://kaikki.org/dictionary/Hebrew/kaikki.org-dictionary-Hebrew.json'

const KEEP_LANGS = new Set(['אנגלית', 'ערבית', 'ארמית'])

// ── Download helpers ──────────────────────────────────────────────────────────

function download(url, dest) {
  return new Promise((resolve, reject) => {
    console.log(`Downloading ${url} ...`)
    const file = fs.createWriteStream(dest)
    const req = https.get(url, (res) => {
      if (res.statusCode === 301 || res.statusCode === 302) {
        file.close()
        fs.unlinkSync(dest)
        return download(res.headers.location, dest).then(resolve).catch(reject)
      }
      if (res.statusCode !== 200) {
        file.close()
        fs.unlinkSync(dest)
        return reject(new Error(`HTTP ${res.statusCode} for ${url}`))
      }
      const isGzip = res.headers['content-encoding'] === 'gzip' ||
        url.endsWith('.gz') || url.endsWith('.jsonl.gz')
      const stream = isGzip ? res.pipe(zlib.createGunzip()) : res
      stream.pipe(file)
      file.on('finish', () => file.close(resolve))
      stream.on('error', reject)
    })
    req.on('error', reject)
  })
}

// ── Text helpers ──────────────────────────────────────────────────────────────

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

function tagsToFilterTag(tags) {
  if (!tags || !tags.length) return null
  const relevant = tags.filter(t => typeof t === 'string' && t.length > 0)
  return relevant.length ? relevant.join(',') : null
}

// ── Import ────────────────────────────────────────────────────────────────────

function importDump(dumpPath) {
  const db = new Database(DST_DB)
  db.pragma('journal_mode = WAL')
  db.pragma('foreign_keys = ON')
  db.pragma('synchronous = NORMAL')

  // Insert the single source row
  db.prepare("INSERT OR IGNORE INTO source (label) VALUES ('ויקימילון')").run()
  const sourceId = db.prepare("SELECT id FROM source WHERE label = 'ויקימילון'").get().id

  const insertSense = db.prepare(`
    INSERT OR IGNORE INTO sense
      (headword, nikud, pos, binyan, shoresh, ktiv_male, etymology, source_id, sense_order)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `)
  const getSenseId = db.prepare(
    'SELECT id FROM sense WHERE headword = ? AND sense_order = ? LIMIT 1'
  )
  const insertDef = db.prepare(`
    INSERT OR IGNORE INTO definition (sense_id, text, filter_tag, def_order)
    VALUES (?, ?, ?, ?)
  `)
  const insertExample = db.prepare(`
    INSERT OR IGNORE INTO example (definition_id, text, source)
    VALUES (?, ?, ?)
  `)
  const getOrInsertSection = db.prepare(`
    INSERT OR IGNORE INTO section (sense_id, name) VALUES (?, ?)
  `)
  const getSectionId = db.prepare(
    'SELECT id FROM section WHERE sense_id = ? AND name = ? LIMIT 1'
  )
  const insertSectionItem = db.prepare(`
    INSERT OR IGNORE INTO section_item (section_id, text, item_order) VALUES (?, ?, ?)
  `)
  const insertTranslation = db.prepare(`
    INSERT OR IGNORE INTO translation (sense_id, lang, word) VALUES (?, ?, ?)
  `)

  let totalEntries = 0
  let totalSenses = 0
  let totalDefs = 0
  let skipped = 0

  // Group entries by word — kaikki may emit multiple entries per headword
  // (one per language section). We only want Hebrew-language entries.
  const wordSenseMap = new Map() // headword → next sense_order

  const lines = fs.readFileSync(dumpPath, 'utf8').split('\n')
  console.log(`Processing ${lines.length} lines...`)

  const batchSize = 500
  let batch = []

  function processBatch(entries) {
    const tx = db.transaction(() => {
      for (const entry of entries) {
        processEntry(entry)
      }
    })
    tx()
  }

  function processEntry(entry) {
    // Only import entries where the word contains Hebrew characters
    const word = (entry.word || '').trim()
    if (!word || !containsHebrew(word)) { skipped++; return }

    // Skip entries that are not Hebrew language (kaikki includes all languages)
    const lang = (entry.lang_code || '').toLowerCase()
    if (lang && lang !== 'he') { skipped++; return }

    totalEntries++

    const senses = entry.senses || []
    if (!senses.length) return

    // Determine sense_order for this entry (multiple entries per word are sequential)
    if (!wordSenseMap.has(word)) wordSenseMap.set(word, 0)
    const baseSenseOrder = wordSenseMap.get(word)

    // Extract top-level metadata
    const nikud = entry.forms
      ? (entry.forms.find(f => f.tags && f.tags.includes('canonical') && /[\u05B0-\u05C7]/.test(f.form || '')) || {}).form || null
      : null

    const pos = entry.pos || null
    const etymology = entry.etymology_text
      ? entry.etymology_text.slice(0, 300)
      : null

    // Extract shoresh from etymology_templates
    let shoresh = null
    if (entry.etymology_templates) {
      for (const tmpl of entry.etymology_templates) {
        if (tmpl.name === 'שרש' || tmpl.name === 'שרש3') {
          const args = tmpl.args || {}
          const parts = [args['1'], args['2'], args['3']].filter(Boolean)
          if (parts.length >= 2) { shoresh = parts.join('-'); break }
          if (parts.length === 1) { shoresh = parts[0]; break }
        }
      }
    }

    // Extract translations
    const translations = []
    if (entry.translations) {
      for (const t of entry.translations) {
        const lang = t.lang || ''
        if (KEEP_LANGS.has(lang) && t.word) {
          translations.push({ lang, word: t.word })
        }
      }
    }

    // Each kaikki "sense" maps to one definition row under one sense row.
    // We group all senses of this entry under a single sense row (sense_order = baseSenseOrder).
    const senseOrder = baseSenseOrder
    wordSenseMap.set(word, baseSenseOrder + 1)

    const result = insertSense.run(word, nikud, pos, null, shoresh, null, etymology, sourceId, senseOrder)
    const senseId = result.changes > 0
      ? Number(result.lastInsertRowid)
      : getSenseId.get(word, senseOrder)?.id
    if (!senseId) return

    totalSenses++

    let defOrder = 0
    for (const sense of senses) {
      const glosses = sense.glosses || []
      if (!glosses.length) continue

      const text = glosses[glosses.length - 1] // last gloss is most specific
      if (!text || text.length < 2) continue

      // Collect all tags from this sense for filter_tag
      const allTags = [...(sense.tags || []), ...(sense.categories || []).map(c => c.name || c)]
      const filterTag = tagsToFilterTag(allTags.filter(t => typeof t === 'string'))

      const defResult = insertDef.run(senseId, text, filterTag, defOrder)
      const defId = defResult.changes > 0
        ? Number(defResult.lastInsertRowid)
        : null

      if (defId) {
        totalDefs++
        // Examples
        const examples = sense.examples || []
        for (const ex of examples) {
          const exText = ex.text || ex.ref || ''
          if (exText && containsHebrew(exText)) {
            insertExample.run(defId, exText.slice(0, 500), ex.ref || null)
          }
        }
      }

      defOrder++
    }

    // Translations
    for (const t of translations) {
      insertTranslation.run(senseId, t.lang, t.word)
    }

    // Synonyms → section
    if (entry.synonyms && entry.synonyms.length) {
      getOrInsertSection.run(senseId, 'מילים נרדפות')
      const secId = getSectionId.get(senseId, 'מילים נרדפות')?.id
      if (secId) {
        entry.synonyms.forEach((s, i) => {
          const w = s.word || s
          if (typeof w === 'string' && containsHebrew(w)) {
            insertSectionItem.run(secId, w, i)
          }
        })
      }
    }

    // Antonyms → section
    if (entry.antonyms && entry.antonyms.length) {
      getOrInsertSection.run(senseId, 'ניגודים')
      const secId = getSectionId.get(senseId, 'ניגודים')?.id
      if (secId) {
        entry.antonyms.forEach((s, i) => {
          const w = s.word || s
          if (typeof w === 'string' && containsHebrew(w)) {
            insertSectionItem.run(secId, w, i)
          }
        })
      }
    }
  }

  // Process in batches
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim()
    if (!line) continue
    let entry
    try { entry = JSON.parse(line) } catch { continue }
    batch.push(entry)
    if (batch.length >= batchSize) {
      processBatch(batch)
      batch = []
      if (i % 10000 === 0) process.stdout.write(`  ${i}/${lines.length}\r`)
    }
  }
  if (batch.length) processBatch(batch)

  const senseCount = db.prepare('SELECT COUNT(*) as c FROM sense').get().c
  const defCount = db.prepare('SELECT COUNT(*) as c FROM definition').get().c
  const taggedCount = db.prepare('SELECT COUNT(*) as c FROM definition WHERE filter_tag IS NOT NULL').get().c
  db.close()

  console.log(`\nDone.`)
  console.log(`  Entries processed: ${totalEntries}`)
  console.log(`  Skipped (non-Hebrew): ${skipped}`)
  console.log(`  Senses inserted: ${totalSenses}`)
  console.log(`  Definitions inserted: ${totalDefs}`)
  console.log(`  DB sense rows: ${senseCount}`)
  console.log(`  DB definition rows: ${defCount}`)
  console.log(`  Tagged definitions (have filter_tag): ${taggedCount}`)
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  if (!fs.existsSync(DUMP_DIR)) fs.mkdirSync(DUMP_DIR, { recursive: true })

  const localPath = process.env.DUMP_PATH || DUMP_FILE

  if (!fs.existsSync(localPath) && process.env.NO_DOWNLOAD !== '1') {
    await download(DUMP_URL, localPath)
    console.log(`Saved to ${localPath}`)
  } else if (fs.existsSync(localPath)) {
    console.log(`Using existing dump: ${localPath}`)
  } else {
    console.error(`Dump file not found: ${localPath}`)
    console.error('Set DUMP_PATH to point to a local .jsonl file, or remove NO_DOWNLOAD=1')
    process.exit(1)
  }

  // Recreate the schema before importing
  console.log('Recreating schema...')
  require('./create-wikidictionary-db.cjs')

  console.log('Importing...')
  importDump(localPath)
}

main().catch((err) => { console.error(err); process.exit(1) })
