'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })
db.prepare(`
  SELECT s.headword, d.text
  FROM sense s JOIN source src ON src.id=s.source_id
  JOIN definition d ON d.sense_id=s.id
  WHERE src.label='קיצור'
  ORDER BY RANDOM() LIMIT 15
`).all().forEach(r => console.log(`[${r.headword}] → ${r.text}`))
db.close()
