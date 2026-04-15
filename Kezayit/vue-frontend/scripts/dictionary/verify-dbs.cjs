'use strict'
const Database = require('better-sqlite3')

// Test dictionary.db
const dict = new Database('./public/dicts/kezayit_dictionary.db', { readonly: true })
console.log('=== kezayit_dictionary.db ===')
console.log('senses:', dict.prepare('SELECT COUNT(*) as c FROM sense').get().c)
console.log('tables:', dict.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(r => r.name).join(', '))
console.log('sense cols:', dict.prepare('PRAGMA table_info(sense)').all().map(c => c.name).join(', '))
const dictSample = dict.prepare('SELECT headword FROM sense ORDER BY RANDOM() LIMIT 5').all()
console.log('sample:', dictSample.map(r => r.headword).join(', '))
dict.close()

// Test wikidictionary.db
const wiki = new Database('./public/dicts/wikidictionary.db', { readonly: true })
console.log('\n=== wikidictionary.db ===')
console.log('senses:', wiki.prepare('SELECT COUNT(*) as c FROM sense').get().c)
console.log('definitions:', wiki.prepare('SELECT COUNT(*) as c FROM definition').get().c)
console.log('tables:', wiki.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(r => r.name).join(', '))
console.log('sense cols:', wiki.prepare('PRAGMA table_info(sense)').all().map(c => c.name).join(', '))
const wikiSample = wiki.prepare('SELECT headword FROM sense ORDER BY RANDOM() LIMIT 5').all()
console.log('sample:', wikiSample.map(r => r.headword).join(', '))

// Test the actual queries the app uses
const SQL_SUGGEST = 'SELECT s.headword, src.label AS source_label, GROUP_CONCAT(d.text, \', \') AS definition FROM sense s LEFT JOIN source src ON src.id = s.source_id JOIN definition d ON d.sense_id = s.id WHERE s.headword LIKE ? GROUP BY s.headword, s.source_id ORDER BY CASE WHEN s.headword LIKE ? THEN 0 ELSE 1 END, s.headword, s.source_id LIMIT 50'
const suggest = wiki.prepare(SQL_SUGGEST).all('%שלום%', 'שלום%')
console.log('suggest query works:', suggest.length, 'results for שלום')
wiki.close()
