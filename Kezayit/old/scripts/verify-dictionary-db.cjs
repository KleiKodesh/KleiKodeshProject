'use strict'
const db = require('better-sqlite3')('public/dictionary.db')
const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").all()
console.log('Tables:', tables.map((t) => t.name).join(', '))

console.log('\n-- Row counts --')
for (const t of tables) {
  if (t.name.startsWith('sqlite')) continue
  const c = db.prepare(`SELECT COUNT(*) as c FROM ${t.name}`).get()
  console.log(`  ${t.name}: ${c.c}`)
}

console.log('\n-- Sample Aramaic senses --')
const rows = db.prepare(`
  SELECT s.headword, s.nikud, s.source_label, d.text
  FROM sense s JOIN definition d ON d.sense_id = s.id
  LIMIT 8
`).all()
rows.forEach((r) => console.log(`  [${r.source_label}] ${r.headword} (${r.nikud ?? '-'}) → ${r.text}`))

console.log('\n-- Autosuggest test: words starting with אב --')
const sugg = db.prepare(`
  SELECT DISTINCT headword FROM sense WHERE headword LIKE 'אב%' LIMIT 10
`).all()
sugg.forEach((r) => console.log(`  ${r.headword}`))

db.close()
