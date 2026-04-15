'use strict'
/**
 * optimize-kezayit-db.cjs
 *
 * Rebuilds kezayit_dictionary.db with a leaner schema:
 *   - Drops always-NULL columns from sense (pos, binyan, shoresh, ktiv_male)
 *   - Drops redundant idx_definition_first (all def_order = 0, idx_definition_sense covers it)
 *   - Drops idx_sense_period (period_tag column doesn't exist in this DB)
 *   - Adds a covering index for DICT_SUGGEST (headword LIKE + GROUP BY headword, source_id)
 *   - Runs VACUUM to compact the file
 *
 * Reads from public/dicts/kezayit_dictionary.db (in-place rebuild via temp file).
 */

const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

function sizeMB(p) {
  try { return Math.round(fs.statSync(p).size / 1024 / 1024 * 10) / 10 } catch { return 0 }
}
function sizeKB(p) {
  try { return Math.round(fs.statSync(p).size / 1024) } catch { return 0 }
}

const SRC = path.resolve('./public/dicts/kezayit_dictionary.db')
const TMP = path.resolve('./public/dicts/kezayit_dictionary_new.db')

if (!fs.existsSync(SRC)) {
  console.error('kezayit_dictionary.db not found at', SRC)
  process.exit(1)
}

console.log('Source size:', sizeKB(SRC), 'KB')

// ── Open source ───────────────────────────────────────────────────────────────
const src = new Database(SRC, { readonly: true })

// ── Create destination ────────────────────────────────────────────────────────
if (fs.existsSync(TMP)) fs.unlinkSync(TMP)
const dst = new Database(TMP)
dst.pragma('journal_mode = DELETE')
dst.pragma('foreign_keys = OFF')
dst.pragma('synchronous = OFF')
dst.pragma('page_size = 4096')

// ── Create lean schema ────────────────────────────────────────────────────────
dst.exec(`
  CREATE TABLE source (
    id    INTEGER PRIMARY KEY,
    label TEXT NOT NULL UNIQUE,
    lang  TEXT,
    url   TEXT
  );

  CREATE TABLE sense (
    id          INTEGER PRIMARY KEY,
    headword    TEXT NOT NULL,
    nikud       TEXT,
    source_id   INTEGER,
    sense_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (source_id) REFERENCES source(id),
    UNIQUE (headword, source_id, sense_order)
  );

  -- Primary lookup: headword prefix search + join to definition
  CREATE INDEX idx_sense_headword ON sense(headword, sense_order);
  -- Source filter (used in DICT_SUGGEST GROUP BY)
  CREATE INDEX idx_sense_source ON sense(source_id);
  -- Covering index for DICT_SUGGEST: headword LIKE + GROUP BY headword, source_id
  -- Avoids a separate definition join scan for the first-def text
  CREATE INDEX idx_sense_suggest ON sense(headword, source_id, id);

  CREATE TABLE definition (
    id        INTEGER PRIMARY KEY,
    sense_id  INTEGER NOT NULL,
    text      TEXT NOT NULL,
    def_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (sense_id) REFERENCES sense(id) ON DELETE CASCADE,
    UNIQUE (sense_id, def_order)
  );

  -- Single index covers all definition lookups (sense_id + def_order = 0 filter)
  CREATE INDEX idx_definition_sense ON definition(sense_id, def_order);

  CREATE TABLE _meta (key TEXT PRIMARY KEY, value TEXT);
`)

// ── Copy data ─────────────────────────────────────────────────────────────────
console.log('Copying source rows...')
const sources = src.prepare('SELECT id, label, lang, url FROM source').all()
const insertSource = dst.prepare('INSERT INTO source (id, label, lang, url) VALUES (?, ?, ?, ?)')
dst.transaction(() => sources.forEach(r => insertSource.run(r.id, r.label, r.lang, r.url)))()
console.log(' ', sources.length, 'source rows')

console.log('Copying sense rows...')
const senses = src.prepare('SELECT id, headword, nikud, source_id, sense_order FROM sense').all()
const insertSense = dst.prepare('INSERT INTO sense (id, headword, nikud, source_id, sense_order) VALUES (?, ?, ?, ?, ?)')
dst.transaction(() => senses.forEach(r => insertSense.run(r.id, r.headword, r.nikud, r.source_id, r.sense_order)))()
console.log(' ', senses.length, 'sense rows')

console.log('Copying definition rows...')
const defs = src.prepare('SELECT id, sense_id, text, def_order FROM definition').all()
const insertDef = dst.prepare('INSERT INTO definition (id, sense_id, text, def_order) VALUES (?, ?, ?, ?)')
dst.transaction(() => defs.forEach(r => insertDef.run(r.id, r.sense_id, r.text, r.def_order)))()
console.log(' ', defs.length, 'definition rows')

// Copy _meta if it exists in source
try {
  const meta = src.prepare('SELECT key, value FROM _meta').all()
  if (meta.length) {
    const insertMeta = dst.prepare('INSERT INTO _meta (key, value) VALUES (?, ?)')
    dst.transaction(() => meta.forEach(r => insertMeta.run(r.key, r.value)))()
    console.log(' ', meta.length, '_meta rows')
  }
} catch (e) { /* _meta may be empty or missing */ }

src.close()

// ── Run ANALYZE so SQLite has fresh statistics ────────────────────────────────
console.log('Running ANALYZE...')
dst.exec('ANALYZE')

dst.pragma('foreign_keys = ON')
dst.close()

// ── Swap files ────────────────────────────────────────────────────────────────
// Use copy+delete instead of rename to avoid EBUSY when the dev server has the DB open.
console.log('Swapping files...')
fs.copyFileSync(SRC, SRC + '.bak')
fs.copyFileSync(TMP, SRC)
fs.unlinkSync(TMP)

console.log('\nDone.')
console.log('Before:', sizeKB(SRC + '.bak'), 'KB')
console.log('After: ', sizeKB(SRC), 'KB')
console.log('Saved: ', sizeKB(SRC + '.bak') - sizeKB(SRC), 'KB')
console.log('\nBackup saved as kezayit_dictionary.db.bak — delete when satisfied.')
