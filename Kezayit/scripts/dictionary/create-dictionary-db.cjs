'use strict'
/**
 * Creates public/dictionary.db with a clean, normalized schema.
 *
 * Design decisions:
 * - `source` table normalizes the 4 Aramaic dictionary names — no repeated strings
 * - `sense.word` removed — redundant with headword for Aramaic; Wiktionary uses headword as key
 * - `sense.pos` removed from sense — language/pos stored on source for Aramaic (always 'ארמית')
 *   and parsed per-sense for Wiktionary
 * - `cache_entry` tracks live Wiktionary fetches only (not Aramaic DB entries)
 * - All FKs reference the correct tables
 * - Indexes cover every FK and every query pattern used at runtime
 */

const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve(__dirname, '../../public/dicts/dictionary.db'))
db.pragma('journal_mode = WAL')
db.pragma('foreign_keys = OFF') // off during construction

db.exec(`
  DROP TABLE IF EXISTS section_item;
  DROP TABLE IF EXISTS section;
  DROP TABLE IF EXISTS example;
  DROP TABLE IF EXISTS definition;
  DROP TABLE IF EXISTS translation;
  DROP TABLE IF EXISTS sense;
  DROP TABLE IF EXISTS cache_entry;
  DROP TABLE IF EXISTS source;
`)

db.exec(`
  -- ── source ────────────────────────────────────────────────────────────────
  -- Normalized lookup for Aramaic dictionary sources.
  -- Wiktionary entries use source_id = NULL (they come from the live API).
  -- lang: content language/type — 'ארמית' | 'ראשי תיבות' | 'עברית'
  CREATE TABLE source (
    id    INTEGER PRIMARY KEY,
    label TEXT NOT NULL UNIQUE,  -- e.g. 'מילון ארמי א'
    lang  TEXT,                  -- content type: ארמית | ראשי תיבות | עברית
    url   TEXT                   -- source URL; NULL for physical files (Torat Emet)
  );

  -- ── cache_entry ───────────────────────────────────────────────────────────
  -- Tracks live Wiktionary fetches so we can cache results and avoid re-fetching.
  -- Aramaic entries are NOT in this table — they come from the local DB directly.
  CREATE TABLE cache_entry (
    headword   TEXT PRIMARY KEY,
    fetched_at INTEGER NOT NULL,  -- unix timestamp of last fetch
    found      INTEGER NOT NULL DEFAULT 1  -- 0 = confirmed missing on Wiktionary
  );

  -- ── sense ─────────────────────────────────────────────────────────────────
  -- One row per sense of a headword.
  -- For Aramaic: source_id is set, pos/binyan/shoresh/ktiv_male are NULL.
  -- For Wiktionary: source_id is NULL, pos/binyan/shoresh/ktiv_male come from parsed wikitext.
  -- etymology: the (=expansion) note extracted at import time, e.g. '=על לב' for אליבא
  -- cross_ref: abbreviation that was resolved (Aramaic abbrev entries only)
  -- period_tag: language period — NULL for Aramaic/abbrev (not applicable)
  CREATE TABLE sense (
    id          INTEGER PRIMARY KEY,
    headword    TEXT NOT NULL,
    nikud       TEXT,
    pos         TEXT,
    binyan      TEXT,
    shoresh     TEXT,
    ktiv_male   TEXT,
    etymology   TEXT,             -- (=...) expansion extracted at import, NULL if none
    cross_ref   TEXT,             -- resolved abbreviation (Aramaic only), NULL otherwise
    period_tag  TEXT,             -- language period (wiki only): מקרא|חז"ל|ביניים|חדשה|NULL
    source_id   INTEGER,          -- NULL = live Wiktionary
    sense_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (source_id) REFERENCES source(id),
    UNIQUE (headword, source_id, sense_order)
  );

  CREATE INDEX idx_sense_headword ON sense(headword, sense_order);
  CREATE INDEX idx_sense_source   ON sense(source_id);
  CREATE INDEX idx_sense_period   ON sense(period_tag);

  -- ── definition ────────────────────────────────────────────────────────────
  -- filter_tag: raw layer tag from wikitext (e.g. 'גס', 'סלנג', 'המקרא').
  --   NULL = untagged. Consistent name with wikidictionary.db.
  CREATE TABLE definition (
    id         INTEGER PRIMARY KEY,
    sense_id   INTEGER NOT NULL,
    text       TEXT NOT NULL,
    filter_tag TEXT,
    def_order  INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (sense_id) REFERENCES sense(id) ON DELETE CASCADE,
    UNIQUE (sense_id, def_order)
  );

  CREATE INDEX idx_definition_sense ON definition(sense_id, def_order);
  -- Covering index for SEARCH_DICT_SENSES join (sense JOIN definition WHERE def_order=0)
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
  -- Named sections per sense: synonyms, etymology, derivatives, antonyms, phrases.
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

  -- ── _meta ─────────────────────────────────────────────────────────────────
  CREATE TABLE _meta (key TEXT PRIMARY KEY, value TEXT);
`)

db.pragma('foreign_keys = ON')
db.close()
console.log('public/dictionary.db created with clean schema.')
