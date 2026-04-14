'use strict'
const db = require('better-sqlite3')('./public/dicts/wikidictionary.db', { readonly: true })
const fs = require('fs')
console.log('senses:      ', db.prepare('SELECT COUNT(*) as c FROM sense').get().c)
console.log('defs:        ', db.prepare('SELECT COUNT(*) as c FROM definition').get().c)
console.log('tagged defs: ', db.prepare("SELECT COUNT(*) as c FROM definition WHERE filter_tag IS NOT NULL").get().c)
console.log('examples:    ', db.prepare('SELECT COUNT(*) as c FROM example').get().c)
console.log('sections:    ', db.prepare('SELECT COUNT(*) as c FROM section').get().c)
console.log('translations:', db.prepare('SELECT COUNT(*) as c FROM translation').get().c)
console.log('size:        ', Math.round(fs.statSync('./public/dicts/wikidictionary.db').size / 1024 / 1024) + ' MB')

console.log('\nPeriod tag breakdown:')
db.prepare("SELECT period_tag, COUNT(*) as c FROM sense GROUP BY period_tag ORDER BY c DESC").all()
  .forEach(r => console.log(' ', (r.period_tag||'NULL (modern)').padEnd(15), r.c))

console.log('\nTop filter_tags:')
db.prepare("SELECT filter_tag, COUNT(*) as c FROM definition WHERE filter_tag IS NOT NULL GROUP BY filter_tag ORDER BY c DESC LIMIT 10").all()
  .forEach(r => console.log(' ', r.filter_tag.padEnd(20), r.c))

console.log('\nTranslation languages:')
db.prepare("SELECT lang, COUNT(*) as c FROM translation GROUP BY lang ORDER BY c DESC").all()
  .forEach(r => console.log(' ', r.lang.padEnd(15), r.c))

console.log('\nSample entries:')
db.prepare("SELECT s.headword, s.pos, s.shoresh, s.period_tag, d.text, d.filter_tag FROM sense s JOIN definition d ON d.sense_id=s.id AND d.def_order=0 WHERE s.headword IN ('שלום','בית','אמא','ברא','ספר') ORDER BY s.headword").all()
  .forEach(r => console.log(' ', r.headword, `pos=${r.pos||'-'} shoresh=${r.shoresh||'-'} period=${r.period_tag||'-'}`, `| ${r.text.substring(0,50)}`))

db.close()
