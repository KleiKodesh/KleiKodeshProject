'use strict'
// Mini test: run one batch through the full import pipeline
const { execSync } = require('child_process')
const path = require('path')
const Database = require('better-sqlite3')

// Recreate schema
execSync(`node "${path.resolve(__dirname, 'create-wikidictionary-db.cjs')}"`, { stdio: 'inherit' })

const DST_DB = path.resolve(__dirname, '../public/wikidictionary.db')
const db = new Database(DST_DB)
db.pragma('journal_mode = WAL')
db.pragma('synchronous = NORMAL')
db.exec(`CREATE TABLE IF NOT EXISTS _meta (key TEXT PRIMARY KEY, value TEXT)`)
db.prepare("INSERT OR IGNORE INTO source (label) VALUES ('ויקימילון')").run()
const sourceId = db.prepare("SELECT id FROM source WHERE label = 'ויקימילון'").get().id
console.log('sourceId:', sourceId)
console.log('DB path:', DST_DB)

const insertSense = db.prepare(`INSERT OR IGNORE INTO sense (headword, nikud, pos, binyan, shoresh, ktiv_male, etymology, period_tag, source_id, sense_order) VALUES (?,?,?,?,?,?,?,?,?,?)`)

try {
  db.transaction(() => {
    const r = insertSense.run('שלום', null, null, null, null, null, null, null, sourceId, 0)
    console.log('insert changes:', r.changes)
  })()
} catch(e) {
  console.error('TRANSACTION ERROR:', e.message)
}

console.log('senses after transaction:', db.prepare('SELECT COUNT(*) as c FROM sense').get().c)
db.close()
