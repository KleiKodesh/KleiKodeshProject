'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })
db.pragma('foreign_keys = ON')

// FK check
const fkv = db.pragma('foreign_key_check')
console.log(`FK violations: ${fkv.length === 0 ? 'None ✓' : fkv.length}`)

// Verify etymology extraction
console.log('\n── Senses with etymology ──')
db.prepare(`
  SELECT s.headword, s.nikud, s.etymology, d.text
  FROM sense s JOIN definition d ON d.sense_id = s.id
  WHERE s.etymology IS NOT NULL
  LIMIT 15
`).all().forEach(r => console.log(`  [${r.headword}] (=${r.etymology}) → ${r.text}`))

// Verify pos is gone from Aramaic rows
console.log('\n── pos column values ──')
db.prepare('SELECT pos, COUNT(*) as c FROM sense GROUP BY pos').all()
  .forEach(r => console.log(`  pos="${r.pos}": ${r.c}`))

// Verify אזיל multi-sense
console.log('\n── אזיל senses ──')
db.prepare(`
  SELECT s.nikud, s.etymology, s.sense_order, src.label, d.text
  FROM sense s LEFT JOIN source src ON src.id=s.source_id
  JOIN definition d ON d.sense_id=s.id
  WHERE s.headword='אזיל' ORDER BY s.sense_order
`).all().forEach(r => console.log(`  [${r.sense_order}] (${r.nikud??'-'}) ${r.etymology?'(='+r.etymology+') ':''}"${r.text}" [${r.label}]`))

// Verify אליבא etymology
console.log('\n── אליבא ──')
db.prepare(`
  SELECT s.nikud, s.etymology, d.text, src.label
  FROM sense s LEFT JOIN source src ON src.id=s.source_id
  JOIN definition d ON d.sense_id=s.id
  WHERE s.headword='אליבא'
`).all().forEach(r => console.log(`  (=${r.etymology}) "${r.text}" [${r.label}]`))

// Counts
console.log('\n── Counts ──')
for (const t of ['source','sense','definition']) {
  const c = db.prepare(`SELECT COUNT(*) as c FROM ${t}`).get()
  console.log(`  ${t}: ${c.c}`)
}

db.close()
