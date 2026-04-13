'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// Check geresh chars used in Aramaic headwords vs Wikipedia headwords
console.log('-- Aramaic headword with geresh --')
const a = db.prepare("SELECT headword FROM sense WHERE source_id <= 4 AND headword LIKE '%\"%' LIMIT 1").get()
if (a) console.log('chars:', [...a.headword].map(c => `${c} U+${c.charCodeAt(0).toString(16).toUpperCase()}`))

console.log('\n-- Wikipedia headword with geresh --')
const w = db.prepare("SELECT headword FROM sense WHERE source_id = 5 AND headword LIKE '%\"%' LIMIT 1").get()
if (w) console.log('chars:', [...w.headword].map(c => `${c} U+${c.charCodeAt(0).toString(16).toUpperCase()}`))

// Check what the autosuggest query returns for א"ח typed as ASCII "
const typed = 'א"ח'
console.log('\n-- Typed chars --')
console.log([...typed].map(c => `${c} U+${c.charCodeAt(0).toString(16).toUpperCase()}`))

const results = db.prepare("SELECT headword FROM sense WHERE headword LIKE ? LIMIT 5").all(`%${typed}%`)
console.log('\n-- LIKE results --', results)

db.close()
