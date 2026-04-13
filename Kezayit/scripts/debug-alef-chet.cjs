'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

console.log('-- sense rows for א"ח --')
db.prepare(`
  SELECT s.headword, s.source_id, src.label, d.text
  FROM sense s
  LEFT JOIN source src ON src.id = s.source_id
  JOIN definition d ON d.sense_id = s.id
  WHERE s.headword = 'א"ח'
`).all().forEach(r => console.log(JSON.stringify(r)))

console.log('\n-- DICT_SUGGEST query for א --')
db.prepare(`
  SELECT s.headword, src.label AS source_label, GROUP_CONCAT(d.text, ', ') AS definition
  FROM sense s
  LEFT JOIN source src ON src.id = s.source_id
  JOIN definition d ON d.sense_id = s.id
  WHERE s.headword LIKE '%א"ח%'
  GROUP BY s.headword, s.source_id
  ORDER BY CASE WHEN s.headword LIKE 'א"ח%' THEN 0 ELSE 1 END, s.headword, s.source_id
  LIMIT 10
`).all().forEach(r => console.log(JSON.stringify(r)))

db.close()
