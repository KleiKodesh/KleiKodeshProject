'use strict'
/**
 * Audits wikidictionary.db to check:
 * 1. What filter_tag values exist and how common they are
 * 2. What fields are NULL that shouldn't be
 * 3. What we might be missing from the wikitext
 */
const db = require('better-sqlite3')('./public/wikidictionary.db', { readonly: true })

console.log('=== DB COUNTS ===')
console.log('senses:      ', db.prepare('SELECT COUNT(*) as c FROM sense').get().c)
console.log('defs:        ', db.prepare('SELECT COUNT(*) as c FROM definition').get().c)
console.log('examples:    ', db.prepare('SELECT COUNT(*) as c FROM example').get().c)
console.log('sections:    ', db.prepare('SELECT COUNT(*) as c FROM section').get().c)
console.log('section_items:', db.prepare('SELECT COUNT(*) as c FROM section_item').get().c)
console.log('translations:', db.prepare('SELECT COUNT(*) as c FROM translation').get().c)

console.log('\n=== SENSE FIELD COVERAGE ===')
const total = db.prepare('SELECT COUNT(*) as c FROM sense').get().c
const fields = ['nikud','pos','binyan','shoresh','ktiv_male','etymology']
for (const f of fields) {
  const n = db.prepare(`SELECT COUNT(*) as c FROM sense WHERE ${f} IS NOT NULL`).get().c
  console.log(`  ${f.padEnd(12)}: ${n} / ${total} (${Math.round(n/total*100)}%)`)
}

console.log('\n=== FILTER_TAG VALUES (top 30) ===')
const tags = db.prepare(`
  SELECT filter_tag, COUNT(*) as c FROM definition
  WHERE filter_tag IS NOT NULL
  GROUP BY filter_tag ORDER BY c DESC LIMIT 30
`).all()
tags.forEach(r => console.log(`  "${r.filter_tag}" — ${r.c}`))

console.log('\n=== SECTION TYPES ===')
const secs = db.prepare(`SELECT name, COUNT(*) as c FROM section GROUP BY name ORDER BY c DESC`).all()
secs.forEach(r => console.log(`  "${r.name}" — ${r.c}`))

console.log('\n=== TRANSLATION LANGUAGES ===')
const langs = db.prepare(`SELECT lang, COUNT(*) as c FROM translation GROUP BY lang ORDER BY c DESC`).all()
langs.forEach(r => console.log(`  "${r.lang}" — ${r.c}`))

console.log('\n=== SAMPLE: senses with pos ===')
db.prepare(`SELECT headword, pos, binyan, shoresh FROM sense WHERE pos IS NOT NULL LIMIT 8`).all()
  .forEach(r => console.log(`  ${r.headword} | pos=${r.pos} | binyan=${r.binyan||'-'} | shoresh=${r.shoresh||'-'}`))

console.log('\n=== SAMPLE: tagged definitions ===')
db.prepare(`SELECT s.headword, d.filter_tag, d.text FROM definition d JOIN sense s ON s.id=d.sense_id WHERE d.filter_tag IS NOT NULL LIMIT 10`).all()
  .forEach(r => console.log(`  [${r.filter_tag}] ${r.headword}: ${r.text.substring(0,60)}`))

console.log('\n=== SAMPLE: definitions with examples ===')
db.prepare(`SELECT s.headword, d.text, e.text as ex FROM example e JOIN definition d ON d.id=e.definition_id JOIN sense s ON s.id=d.sense_id LIMIT 5`).all()
  .forEach(r => console.log(`  ${r.headword}: ${r.text.substring(0,40)} → ex: ${r.ex.substring(0,50)}`))

db.close()
