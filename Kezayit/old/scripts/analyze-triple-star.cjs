'use strict'
const db = require('better-sqlite3')('dist/dictionary.db', { readonly: true })

// How many aramaic entries contain ***
const withStars = db.prepare("SELECT COUNT(*) as c FROM entry WHERE type='aramaic' AND definition LIKE '%***%'").get()
const total = db.prepare("SELECT COUNT(*) as c FROM entry WHERE type='aramaic'").get()
console.log(`Entries with ***: ${withStars.c} / ${total.c}`)

// Sample entries with *** to understand the pattern
console.log('\n── Sample entries with *** ──')
db.prepare("SELECT headword, nikud, definition FROM entry WHERE type='aramaic' AND definition LIKE '%***%' LIMIT 20").all()
  .forEach(r => console.log(`  [${r.headword}] ${r.nikud ?? '-'}\n    → ${r.definition}\n`))

// How many *** per entry (max senses)
console.log('── *** count distribution ──')
db.prepare("SELECT (length(definition) - length(replace(definition,'***',''))) / 3 AS star_count, COUNT(*) as c FROM entry WHERE type='aramaic' GROUP BY star_count ORDER BY star_count").all()
  .forEach(r => console.log(`  ${r.star_count} separators: ${r.c} entries`))

// Check the {nikud} pattern inside definitions
console.log('\n── Entries with {nikud} in definition ──')
db.prepare("SELECT headword, definition FROM entry WHERE type='aramaic' AND definition LIKE '%{%}%' LIMIT 10").all()
  .forEach(r => console.log(`  [${r.headword}] → ${r.definition}`))

// Check (=...) pattern
console.log('\n── Entries with (=...) expansion ──')
db.prepare("SELECT headword, definition FROM entry WHERE type='aramaic' AND definition LIKE '%(=%' LIMIT 10").all()
  .forEach(r => console.log(`  [${r.headword}] → ${r.definition}`))

// Full entry for דילמא
console.log('\n── דילמא full entry ──')
db.prepare("SELECT * FROM entry WHERE headword='דילמא' AND type='aramaic'").all()
  .forEach(r => console.log(JSON.stringify(r)))

db.close()
