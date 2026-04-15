'use strict'
/**
 * verify-kezayit-db.cjs
 * Verifies the optimized kezayit_dictionary.db is healthy and all runtime queries work.
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const dbPath = path.resolve('./public/dicts/kezayit_dictionary.db')
const db = new Database(dbPath, { readonly: true })

let ok = true
function check(label, fn) {
  try {
    const result = fn()
    console.log('OK  ', label, typeof result === 'number' ? '(' + result + ' rows)' : '')
  } catch (e) {
    console.error('FAIL', label, '-', e.message)
    ok = false
  }
}

// Schema
check('sense columns (no pos/binyan/shoresh/ktiv_male)', () => {
  const cols = db.pragma('table_info(sense)').map(c => c.name)
  const dropped = ['pos', 'binyan', 'shoresh', 'ktiv_male']
  const found = dropped.filter(c => cols.includes(c))
  if (found.length) throw new Error('Columns still present: ' + found.join(', '))
  const expected = ['id', 'headword', 'nikud', 'source_id', 'sense_order']
  const missing = expected.filter(c => !cols.includes(c))
  if (missing.length) throw new Error('Missing columns: ' + missing.join(', '))
  return cols.join(', ')
})

check('indexes present', () => {
  const idxs = db.prepare("SELECT name FROM sqlite_master WHERE type='index'").all().map(r => r.name)
  const required = ['idx_sense_headword', 'idx_sense_source', 'idx_sense_suggest', 'idx_definition_sense']
  const missing = required.filter(i => !idxs.includes(i))
  if (missing.length) throw new Error('Missing indexes: ' + missing.join(', '))
  const dropped = ['idx_definition_first', 'idx_sense_period']
  const stillPresent = dropped.filter(i => idxs.includes(i))
  if (stillPresent.length) throw new Error('Redundant indexes still present: ' + stillPresent.join(', '))
})

check('row counts', () => {
  const sc = db.prepare('SELECT COUNT(*) as c FROM sense').get().c
  const dc = db.prepare('SELECT COUNT(*) as c FROM definition').get().c
  if (sc !== 15156) throw new Error('Expected 15156 senses, got ' + sc)
  if (dc !== 15156) throw new Error('Expected 15156 definitions, got ' + dc)
  return sc
})

// SEARCH_DICT_SENSES (pos returned as NULL literal)
check('SEARCH_DICT_SENSES query', () => {
  const rows = db.prepare(`
    SELECT s.id, s.headword, s.nikud, NULL AS pos, src.label AS source_label, d.text AS definition
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword = ? OR s.headword LIKE ?
    ORDER BY CASE WHEN s.headword = ? THEN 0 ELSE 1 END, length(s.headword), s.headword
    LIMIT 100
  `).all('אבא', 'אבא%', 'אבא')
  return rows.length
})

// DICT_SUGGEST
check('DICT_SUGGEST query', () => {
  const rows = db.prepare(`
    SELECT s.headword, src.label AS source_label, d.text AS definition
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword LIKE ?
    GROUP BY s.headword, s.source_id
    ORDER BY CASE WHEN s.headword LIKE ? THEN 0 ELSE 1 END, s.headword, s.source_id
    LIMIT 50
  `).all('%דיל%', 'דיל%')
  return rows.length
})

// GET_DICT_SENSES_FOR_WORD (pos/binyan/shoresh/ktiv_male as NULL literals)
check('GET_DICT_SENSES_FOR_WORD query', () => {
  const rows = db.prepare(`
    SELECT s.id, s.headword, s.nikud,
           NULL AS pos, NULL AS binyan, NULL AS shoresh, NULL AS ktiv_male,
           src.label AS source_label, s.sense_order
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    WHERE s.headword = ?
    ORDER BY s.sense_order
  `).all('אבא')
  return rows.length
})

// GET_DICT_ALL_DEFINITIONS
check('GET_DICT_ALL_DEFINITIONS query', () => {
  const ids = db.prepare('SELECT id FROM sense LIMIT 5').all().map(r => r.id)
  const ph = ids.map(() => '?').join(',')
  const rows = db.prepare(`SELECT id, sense_id, text, def_order FROM definition WHERE sense_id IN (${ph}) ORDER BY sense_id, def_order`).all(...ids)
  return rows.length
})

// EXPLAIN QUERY PLAN for DICT_SUGGEST — should use index
check('DICT_SUGGEST uses index (not full scan)', () => {
  const plan = db.prepare(`
    EXPLAIN QUERY PLAN
    SELECT s.headword, src.label AS source_label, d.text AS definition
    FROM sense s
    LEFT JOIN source src ON src.id = s.source_id
    JOIN definition d ON d.sense_id = s.id AND d.def_order = 0
    WHERE s.headword LIKE ?
    GROUP BY s.headword, s.source_id
    LIMIT 50
  `).all('%דיל%')
  const planText = plan.map(r => r.detail || r.opcode || JSON.stringify(r)).join(' | ')
  console.log('    Plan:', planText)
  if (planText.includes('SCAN sense') && !planText.includes('USING INDEX')) {
    throw new Error('Full table scan on sense — index not used')
  }
})

db.close()

console.log('\nFile size:', Math.round(fs.statSync(dbPath).size / 1024), 'KB')
console.log(ok ? '\nAll checks passed.' : '\nSome checks FAILED.')
process.exit(ok ? 0 : 1)
