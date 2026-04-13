'use strict'
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve(__dirname, '../public/dictionary.db'))
db.pragma('journal_mode = WAL')

db.exec(`
  -- Covers DICT_SUGGEST ORDER BY length(headword), headword — eliminates temp B-tree
  DROP INDEX IF EXISTS idx_sense_headword;
  CREATE INDEX IF NOT EXISTS idx_sense_headword ON sense(headword, length(headword));

  -- Covers GET_DICT_SENSES_FOR_WORD ORDER BY sense_order — eliminates temp B-tree
  DROP INDEX IF EXISTS idx_sense_word;
  CREATE INDEX IF NOT EXISTS idx_sense_word ON sense(word, sense_order);

  -- Covers GET_DICT_DEFINITIONS_FOR_SENSE ORDER BY def_order — eliminates temp B-tree
  DROP INDEX IF EXISTS idx_definition_sense;
  CREATE INDEX IF NOT EXISTS idx_definition_sense ON definition(sense_id, def_order);

  -- Covers SEARCH_DICT_SENSES join: definition.sense_id + def_order=0 filter
  CREATE INDEX IF NOT EXISTS idx_definition_sense_first ON definition(sense_id, def_order, text);
`)

db.close()
console.log('Performance indexes added.')
