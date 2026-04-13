'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

db.pragma('foreign_keys = ON')

console.log('── Foreign key violations ──')
const fkv = db.pragma('foreign_key_check')
console.log(fkv.length ? `  ${fkv.length} violations!` : '  None ✓')

console.log('\n── Row counts ──')
for (const t of ['source','cache_entry','sense','definition','example','section','section_item','translation']) {
  const c = db.prepare(`SELECT COUNT(*) as c FROM ${t}`).get()
  console.log(`  ${t}: ${c.c}`)
}

console.log('\n── source table ──')
db.prepare('SELECT * FROM source').all().forEach(r => console.log(`  ${r.id}: ${r.label}`))

console.log('\n── source_id cardinality in sense ──')
db.prepare('SELECT src.label, COUNT(*) as c FROM sense s LEFT JOIN source src ON src.id = s.source_id GROUP BY s.source_id ORDER BY c DESC').all()
  .forEach(r => console.log(`  "${r.label}": ${r.c}`))

console.log('\n── pos cardinality ──')
db.prepare('SELECT pos, COUNT(*) as c FROM sense GROUP BY pos ORDER BY c DESC').all()
  .forEach(r => console.log(`  "${r.pos}": ${r.c}`))

console.log('\n── NULL analysis ──')
const total = db.prepare('SELECT COUNT(*) as c FROM sense').get().c
for (const col of ['nikud','pos','binyan','shoresh','ktiv_male','source_id']) {
  const n = db.prepare(`SELECT COUNT(*) as c FROM sense WHERE ${col} IS NULL`).get()
  console.log(`  sense.${col}: ${n.c}/${total} NULL (${Math.round(n.c/total*100)}%)`)
}

console.log('\n── Redundant indexes check ──')
const indexes = db.prepare("SELECT name, tbl_name FROM sqlite_master WHERE type='index' ORDER BY tbl_name, name").all()
for (const idx of indexes) {
  const info = db.prepare(`PRAGMA index_info(${idx.name})`).all()
  console.log(`  [${idx.tbl_name}] ${idx.name}: (${info.map(c => c.name).join(', ')})`)
}

console.log('\n── Sample sense+source join ──')
db.prepare('SELECT s.headword, s.nikud, s.pos, src.label, d.text FROM sense s LEFT JOIN source src ON src.id=s.source_id JOIN definition d ON d.sense_id=s.id LIMIT 5').all()
  .forEach(r => console.log(`  [${r.label}] ${r.headword} (${r.nikud??'-'}) → ${r.text}`))

db.close()
