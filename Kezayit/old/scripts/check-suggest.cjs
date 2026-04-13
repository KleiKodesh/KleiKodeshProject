'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// What the suggest query should return for דיל prefix
const rows = db.prepare(`
  SELECT s.headword, src.label, GROUP_CONCAT(d.text, ', ') AS defs
  FROM sense s
  LEFT JOIN source src ON src.id = s.source_id
  JOIN definition d ON d.sense_id = s.id
  WHERE s.headword LIKE 'דיל%'
  GROUP BY s.headword, s.source_id
  ORDER BY s.headword, s.source_id
`).all()

rows.forEach(r => console.log(`[${r.headword}] [${r.label}] -> ${r.defs}`))
db.close()
