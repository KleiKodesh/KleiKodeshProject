'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

const senseIds = db.prepare("SELECT id FROM sense WHERE word='אבא'").all().map(r => r.id)
const defIds = senseIds.length
  ? db.prepare(`SELECT id FROM definition WHERE sense_id IN (${senseIds.map(()=>'?').join(',')})`).all(...senseIds).map(r => r.id)
  : []

const queries = {
  'DICT_SUGGEST': [`SELECT DISTINCT headword FROM sense WHERE headword LIKE ? ORDER BY length(headword), headword LIMIT 30`, ['אב%']],
  'GET_DICT_SENSES_FOR_WORD': [`SELECT id, headword, nikud, pos, binyan, shoresh, ktiv_male, source_label, sense_order FROM sense WHERE word = ? ORDER BY sense_order`, ['אבא']],
  'GET_DICT_ALL_DEFINITIONS (bulk)': [`SELECT id, sense_id, text, layer, def_order FROM definition WHERE sense_id IN (${senseIds.map(()=>'?').join(',')}) ORDER BY sense_id, def_order`, senseIds],
  'GET_DICT_ALL_EXAMPLES (bulk)': [`SELECT e.definition_id, e.text, e.source FROM example e JOIN definition d ON d.id = e.definition_id WHERE d.sense_id IN (${senseIds.map(()=>'?').join(',')}) ORDER BY e.definition_id, e.id`, senseIds],
  'GET_DICT_ALL_SECTIONS (bulk)': [`SELECT s.sense_id, s.name AS section_name, si.text AS item_text, si.item_order FROM section s JOIN section_item si ON si.section_id = s.id WHERE s.sense_id IN (${senseIds.map(()=>'?').join(',')}) ORDER BY s.sense_id, s.id, si.item_order`, senseIds],
  'GET_DICT_ALL_TRANSLATIONS (bulk)': [`SELECT sense_id, lang, word FROM translation WHERE sense_id IN (${senseIds.map(()=>'?').join(',')}) ORDER BY sense_id, lang, id`, senseIds],
  'SEARCH_DICT_SENSES': [`SELECT s.id, s.headword, s.nikud, s.pos, s.source_label, d.text AS definition FROM sense s JOIN definition d ON d.sense_id = s.id AND d.def_order = 0 WHERE s.headword = ? OR s.headword LIKE ? ORDER BY CASE WHEN s.headword = ? THEN 0 ELSE 1 END, length(s.headword), s.headword LIMIT 100`, ['אבא', 'אבא%', 'אבא']],
}

console.log('── Query plans after index improvements ──')
for (const [name, [sql, params]] of Object.entries(queries)) {
  if (!params.length) continue
  console.log(`\n  ${name}`)
  try {
    const plan = db.prepare(`EXPLAIN QUERY PLAN ${sql}`).all(...params)
    plan.forEach(r => console.log(`    ${r.detail}`))
  } catch(e) { console.log(`    ERROR: ${e.message}`) }
}

console.log('\n── Round-trips: before vs after ──')
console.log('  Before: 1 + (4 × senses) + (1 × defs) = N+1 per word')
console.log('  After:  1 (senses) + 4 parallel (defs/examples/sections/translations) = 5 total, always')

db.close()
