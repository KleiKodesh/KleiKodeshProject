'use strict'
const Database = require('better-sqlite3')
const fs = require('fs')
const db = new Database('./public/dicts/wikidictionary.db', { readonly: true })
const rows = db.prepare(`
  SELECT s.headword, GROUP_CONCAT(d.text, ' | ') as defs
  FROM sense s
  JOIN definition d ON d.sense_id = s.id
  GROUP BY s.headword
  ORDER BY s.headword
`).all()
console.log('total:', rows.length)
fs.writeFileSync('./scripts/dictionary/wordlist.json', JSON.stringify(rows.map(r => ({ w: r.headword, d: r.defs.substring(0, 150) })), null, 0))
console.log('written to scripts/dictionary/wordlist.json')
db.close()
