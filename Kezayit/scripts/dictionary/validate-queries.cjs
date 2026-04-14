'use strict'
/**
 * Validates all SQL queries in queries.sql.ts against the actual DB schemas.
 * Runs each query with dummy params and reports any errors.
 */
const Database = require('better-sqlite3')

const kezayit = new Database('./public/dicts/kezayit_dictionary.db', { readonly: true })
const wiki = new Database('./public/dicts/wikidictionary.db', { readonly: true })

const tests = [
  // ── Kezayit dictionary queries ──
  { db: kezayit, name: 'SEARCH_DICT_SENSES',
    sql: `SELECT s.id, s.headword, s.nikud, s.pos, src.label AS source_label, d.text AS definition FROM sense s LEFT JOIN source src ON src.id = s.source_id JOIN definition d ON d.sense_id = s.id AND d.def_order = 0 WHERE s.headword = ? OR s.headword LIKE ? ORDER BY CASE WHEN s.headword = ? THEN 0 ELSE 1 END, length(s.headword), s.headword LIMIT 100`,
    params: ['test', 'test%', 'test'] },
  { db: kezayit, name: 'DICT_SUGGEST',
    sql: `SELECT s.headword, src.label AS source_label, GROUP_CONCAT(d.text, ', ') AS definition FROM sense s LEFT JOIN source src ON src.id = s.source_id JOIN definition d ON d.sense_id = s.id WHERE s.headword LIKE ? GROUP BY s.headword, s.source_id ORDER BY CASE WHEN s.headword LIKE ? THEN 0 ELSE 1 END, s.headword, s.source_id LIMIT 50`,
    params: ['%test%', 'test%'] },
  { db: kezayit, name: 'GET_DICT_SENSES_FOR_WORD',
    sql: `SELECT s.id, s.headword, s.nikud, s.pos, s.binyan, s.shoresh, s.ktiv_male, src.label AS source_label, s.sense_order FROM sense s LEFT JOIN source src ON src.id = s.source_id WHERE s.headword = ? ORDER BY s.sense_order`,
    params: ['test'] },
  { db: kezayit, name: 'GET_DICT_ALL_DEFINITIONS',
    sql: `SELECT id, sense_id, text, def_order FROM definition WHERE sense_id IN (1,2,3) ORDER BY sense_id, def_order`,
    params: [] },

  // ── Wikidictionary queries ──
  { db: wiki, name: 'WIKIDICT_SUGGEST',
    sql: `SELECT s.headword, d.text AS definition FROM sense s JOIN definition d ON d.sense_id = s.id AND d.def_order = 0 WHERE s.headword LIKE ? OR s.ktiv_male LIKE ? GROUP BY s.headword ORDER BY CASE WHEN s.headword LIKE ? OR s.ktiv_male LIKE ? THEN 0 ELSE 1 END, s.headword LIMIT 50`,
    params: ['%test%', '%test%', 'test%', 'test%'] },
  { db: wiki, name: 'GET_WIKIDICT_SENSES_FOR_WORD',
    sql: `SELECT s.id, s.headword, s.nikud, s.pos, s.binyan, s.shoresh, s.ktiv_male, src.label AS source_label, s.sense_order FROM sense s JOIN source src ON src.id = s.source_id WHERE s.headword = ? OR s.ktiv_male = ? ORDER BY s.sense_order`,
    params: ['test', 'test'] },
  { db: wiki, name: 'GET_WIKIDICT_ALL_DEFINITIONS',
    sql: `SELECT id, sense_id, text, def_order FROM definition WHERE sense_id IN (1,2,3) ORDER BY sense_id, def_order`,
    params: [] },
  { db: wiki, name: 'GET_WIKIDICT_ALL_EXAMPLES',
    sql: `SELECT e.definition_id, e.text, e.source FROM example e JOIN definition d ON d.id = e.definition_id WHERE d.sense_id IN (1,2,3) ORDER BY e.definition_id, e.id`,
    params: [] },
  { db: wiki, name: 'GET_WIKIDICT_ALL_SECTIONS',
    sql: `SELECT s.sense_id, s.name AS section_name, si.text AS item_text, si.item_order FROM section s JOIN section_item si ON si.section_id = s.id WHERE s.sense_id IN (1,2,3) ORDER BY s.sense_id, s.id, si.item_order`,
    params: [] },
]

let ok = 0, fail = 0
for (const t of tests) {
  try {
    t.db.prepare(t.sql).all(...t.params)
    console.log('✓', t.name)
    ok++
  } catch(e) {
    console.error('✗', t.name, '->', e.message)
    fail++
  }
}
console.log(`\n${ok} passed, ${fail} failed`)
kezayit.close()
wiki.close()
