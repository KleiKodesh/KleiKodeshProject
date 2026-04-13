'use strict'
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve(__dirname, '../dist/dictionary.db'), { readonly: true })

// What tables exist
const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").all()
console.log('Tables:', tables.map((t) => t.name).join(', '))

// Schema of each table
for (const t of tables) {
  const info = db.prepare(`PRAGMA table_info(${t.name})`).all()
  console.log(`\n-- ${t.name} --`)
  info.forEach((c) => console.log(`  ${c.name} ${c.type} ${c.notnull ? 'NOT NULL' : ''} ${c.dflt_value ?? ''}`))
}

// Aramaic sources breakdown
console.log('\n-- Aramaic by source --')
const src = db.prepare("SELECT source, COUNT(*) as c, MIN(headword) as sample FROM entry WHERE type='aramaic' GROUP BY source ORDER BY source").all()
src.forEach((r) => console.log(`  source ${r.source} (${r.c} entries) e.g. ${r.sample}`))

db.close()
