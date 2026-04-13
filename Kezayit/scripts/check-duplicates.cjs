'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// Duplicate senses: same word + same source_label
console.log('-- Duplicate senses (same headword + source_label) --')
const dupSenses = db.prepare(`
  SELECT headword, source_label, COUNT(*) as c
  FROM sense
  GROUP BY headword, source_label
  HAVING c > 1
  ORDER BY c DESC
  LIMIT 20
`).all()
if (dupSenses.length === 0) console.log('  None')
else dupSenses.forEach((r) => console.log(`  "${r.headword}" [${r.source_label}] × ${r.c}`))

// Duplicate definitions: same sense_id + same text
console.log('\n-- Duplicate definitions (same sense_id + text) --')
const dupDefs = db.prepare(`
  SELECT sense_id, text, COUNT(*) as c
  FROM definition
  GROUP BY sense_id, text
  HAVING c > 1
  ORDER BY c DESC
  LIMIT 20
`).all()
if (dupDefs.length === 0) console.log('  None')
else dupDefs.forEach((r) => console.log(`  sense ${r.sense_id}: "${r.text}" × ${r.c}`))

// Same headword appearing across multiple sources (not a bug, just info)
console.log('\n-- Headwords with entries in multiple sources --')
const multiSrc = db.prepare(`
  SELECT headword, COUNT(DISTINCT source_label) as src_count, GROUP_CONCAT(DISTINCT source_label) as sources
  FROM sense
  GROUP BY headword
  HAVING src_count > 1
  ORDER BY src_count DESC
  LIMIT 10
`).all()
if (multiSrc.length === 0) console.log('  None')
else multiSrc.forEach((r) => console.log(`  "${r.headword}" in ${r.src_count} sources: ${r.sources}`))

// Total counts
const total = db.prepare('SELECT COUNT(*) as c FROM sense').get()
const totalDef = db.prepare('SELECT COUNT(*) as c FROM definition').get()
console.log(`\nTotal senses: ${total.c}, Total definitions: ${totalDef.c}`)

db.close()
