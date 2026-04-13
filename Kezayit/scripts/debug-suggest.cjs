'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// Simulate the exact DICT_SUGGEST query with %דלמ%
console.log('── DICT_SUGGEST with %דלמ% ──')
db.prepare(`
  SELECT s.headword, src.label AS source_label,
         GROUP_CONCAT(d.text, ', ') AS definition
  FROM sense s
  LEFT JOIN source src ON src.id = s.source_id
  JOIN definition d ON d.sense_id = s.id
  WHERE s.headword LIKE ?
  GROUP BY s.headword, s.source_id
  ORDER BY s.headword, s.source_id
  LIMIT 50
`).all('%דלמ%').forEach(r => console.log(`  [${r.headword}] [${r.source_label}] -> ${r.definition}`))

// How many total results?
const total = db.prepare("SELECT COUNT(DISTINCT s.headword || '|' || COALESCE(s.source_id,'')) as c FROM sense s WHERE s.headword LIKE '%דלמ%'").get()
console.log('\nTotal rows matching %דלמ%:', total.c)
