'use strict'
/**
 * Creates public/wikidictionary.db with the same normalized schema as dictionary.db,
 * plus a `filter_tag` column on `definition` to store the raw layer tag from wikitext.
 *
 * The filter_tag preserves the original tag string (e.g. 'גס', 'סלנג', 'ספרות') so
 * the app can apply or relax filtering rules in the future without re-importing.
 *
 * Usage: node scripts/create-wikidictionary-db.cjs
 */

const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve(__dirname, '../public/wikidictionary.db'))
db.pragma('journal_mode = WAL')
db.pragma('foreign_keys = OFF')

db.exec(`
  DROP TABLE IF EXISTS section_item;
  DROP TABLE IF EXISTS section;
  DROP TABLE IF EXISTS example;
  DROP TABLE IF EXISTS definition;
  DROP TABLE IF EXISTS translation;
  DROP TABLE IF EXISTS sense;
  DROP TABLE IF EXISTS source;
`)

db.exec(`
  -- ── source ────────────────────────────────────────────────────────────────
  -- For Wiktionary the only source is 'ויקימילון' (he.wiktionary.org).
  -- Kept as a table for schema consistency with dictionary.db.
  CREATE TABLE source (
    id    INTEGER PRIMARY KEY,
    label TEXT NOT NULL UNIQUE
  );

  -- ── sense ─────────────────────────────────────────────────────────────────
  -- One row per sense of a headword (one == block in wikitext).
  -- nikud: the vocalized form from the sense header, if present.
  -- pos: part of speech (חלק דיבר) from ניתוח דקדוקי template.
  -- binyan: verb binyan, if applicable.
  -- shoresh: root letters (שרש template), e.g. 'ש-ל-מ'.
  -- ktiv_male: full spelling without nikud, if given.
  -- etymology: (=expansion) note or גיזרון section text.
  -- source_id: always points to the single 'ויקימילון' source row.
  -- sense_order: 0-based index within the headword (multiple == blocks).
  CREATE TABLE sense (
    id          INTEGER PRIMARY KEY,
    headword    TEXT NOT NULL,
    nikud       TEXT,
    pos         TEXT,
    binyan      TEXT,
    shoresh     TEXT,
    ktiv_male   TEXT,
    etymology   TEXT,
    source_id   INTEGER NOT NULL,
    sense_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (source_id) REFERENCES source(id),
    UNIQUE (headword, sense_order)
  );

  CREATE INDEX idx_sense_headword ON sense(headword, sense_order);
  CREATE INDEX idx_sense_source   ON sense(source_id);

  -- ── definition ────────────────────────────────────────────────────────────
  -- filter_tag: the raw layer tag string from the wikitext (e.g. 'גס', 'סלנג',
  --   'ספרות', 'עברית מקראית'). NULL means no tag — untagged definitions are
  --   the majority and are always appropriate. Stored here so filtering rules
  --   can be changed without re-importing the data.
  CREATE TABLE definition (
    id         INTEGER PRIMARY KEY,
    sense_id   INTEGER NOT NULL,
    text       TEXT NOT NULL,
    filter_tag TEXT,              -- raw layer tag; NULL = untagged
    def_order  INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (sense_id) REFERENCES sense(id) ON DELETE CASCADE,
    UNIQUE (sense_id, def_order)
  );

  CREATE INDEX idx_definition_sense ON definition(sense_id, def_order);
  CREATE INDEX idx_definition_first ON definition(sense_id, def_order, text)
    WHERE def_order = 0;

  -- ── example ───────────────────────────────────────────────────────────────
  CREATE TABLE example (
    id            INTEGER PRIMARY KEY,
    definition_id INTEGER NOT NULL,
    text          TEXT NOT NULL,
    source        TEXT,
    FOREIGN KEY (definition_id) REFERENCES definition(id) ON DELETE CASCADE
  );

  CREATE INDEX idx_example_def ON example(definition_id);

  -- ── section ───────────────────────────────────────────────────────────────
  -- Named sections per sense: גיזרון, נגזרות, מילים נרדפות, ניגודים, צירופים, etc.
  CREATE TABLE section (
    id       INTEGER PRIMARY KEY,
    sense_id INTEGER NOT NULL,
    name     TEXT NOT NULL,
    FOREIGN KEY (sense_id) REFERENCES sense(id) ON DELETE CASCADE,
    UNIQUE (sense_id, name)
  );

  CREATE INDEX idx_section_sense ON section(sense_id);

  -- ── section_item ──────────────────────────────────────────────────────────
  CREATE TABLE section_item (
    id         INTEGER PRIMARY KEY,
    section_id INTEGER NOT NULL,
    text       TEXT NOT NULL,
    item_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (section_id) REFERENCES section(id) ON DELETE CASCADE
  );

  CREATE INDEX idx_section_item ON section_item(section_id, item_order);

  -- ── translation ───────────────────────────────────────────────────────────
  CREATE TABLE translation (
    id       INTEGER PRIMARY KEY,
    sense_id INTEGER NOT NULL,
    lang     TEXT NOT NULL,
    word     TEXT NOT NULL,
    FOREIGN KEY (sense_id) REFERENCES sense(id) ON DELETE CASCADE,
    UNIQUE (sense_id, lang, word)
  );

  CREATE INDEX idx_translation_sense ON translation(sense_id, lang);
`)

db.pragma('foreign_keys = ON')
db.close()
console.log('public/wikidictionary.db created with clean schema.')
