'use strict'
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve(__dirname, '../public/dictionary.db'))
db.pragma('journal_mode = WAL')
db.pragma('foreign_keys = OFF')

// SQLite can't add a unique constraint to an existing column directly —
// we recreate the sense table with the constraint baked in.

db.exec(`
  -- Rename old table
  ALTER TABLE sense RENAME TO sense_old;

  -- Recreate with unique constraint on (word, source_label, sense_order)
  -- This prevents duplicate senses for the same word from the same source
  CREATE TABLE sense (
    id          INTEGER PRIMARY KEY,
    word        TEXT NOT NULL,
    headword    TEXT NOT NULL,
    nikud       TEXT,
    pos         TEXT,
    binyan      TEXT,
    shoresh     TEXT,
    ktiv_male   TEXT,
    source_label TEXT,
    sense_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (word) REFERENCES cache_entry(word) ON DELETE CASCADE,
    UNIQUE (word, source_label, sense_order)
  );

  -- Copy data
  INSERT INTO sense SELECT * FROM sense_old;

  -- Drop old table
  DROP TABLE sense_old;

  -- Recreate indexes
  CREATE INDEX IF NOT EXISTS idx_sense_word     ON sense(word);
  CREATE INDEX IF NOT EXISTS idx_sense_headword ON sense(headword);

  -- Unique constraint on definition: same sense can't have the same text twice
  CREATE UNIQUE INDEX IF NOT EXISTS idx_definition_unique ON definition(sense_id, text);

  -- Unique constraint on translation: same sense + lang + word
  CREATE UNIQUE INDEX IF NOT EXISTS idx_translation_unique ON translation(sense_id, lang, word);
`)

db.pragma('foreign_keys = ON')

// Verify
const cols = db.prepare("PRAGMA table_info(sense)").all()
console.log('sense columns:', cols.map((c) => c.name).join(', '))
const indexes = db.prepare("SELECT name FROM sqlite_master WHERE type='index' ORDER BY name").all()
console.log('indexes:', indexes.map((i) => i.name).join(', '))
const count = db.prepare('SELECT COUNT(*) as c FROM sense').get()
console.log(`sense rows: ${count.c}`)

db.close()
console.log('Done — unique constraints added.')
