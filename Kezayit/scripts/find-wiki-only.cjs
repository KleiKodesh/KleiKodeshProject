'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })
const rows = db.prepare(`
  SELECT s.headword, d.text
  FROM sense s
  JOIN source src ON src.id = s.source_id
  JOIN definition d ON d.sense_id = s.id
  WHERE src.label = 'ויקיפדיה'
    AND s.headword NOT IN (
      SELECT headword FROM sense WHERE source_id != src.id
    )
  ORDER BY s.headword
  LIMIT 20
`).all()
rows.forEach(r => console.log(`[${r.headword}] → ${r.text}`))
db.close()
