'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })
db.prepare(`
  SELECT s.headword, s.pos, src.label, d.text
  FROM sense s JOIN source src ON src.id=s.source_id
  JOIN definition d ON d.sense_id=s.id
  WHERE s.headword = 'ס"ת'
`).all().forEach(r => console.log(JSON.stringify(r)))
db.close()
