'use strict'
/**
 * Full pipeline: filter + compact wikidictionary.db from backup.
 * Reads from data/dictionaries/wikidictionary.db (original)
 * Writes filtered+compacted result to public/dicts/wikidictionary.db
 */

const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const SRC = path.resolve('./data/dictionaries/wikidictionary.db')
const DST = path.resolve('./public/dicts/wikidictionary.db')
const TMP = path.resolve('./public/dicts/wikidictionary_new.db')

const BLOCKED_TAGS = new Set([
  'סלנג', 'סלנג ישן', 'סלנג ירוד', 'סלנג ישיבות', 'סלנג, צה"ל', 'סלנג להט"בי',
  'עממי', 'כינוי גנאי', 'משלב חסר', 'שפת הדיבור', 'לשון מדוברת', 'דיבור',
  'עגה צה"לית', 'עגה ירושלמית', 'צה"ל', 'צבא',
  'נצרות', 'מיתולוגיה', 'אידאולוגיות',
])

function sizeMB(p) { try { return Math.round(fs.statSync(p).size/1024/1024*10)/10 } catch { return 0 } }

function genericCopy(src, dst, tableName, rows) {
  if (!rows.length) return
  const cols = Object.keys(rows[0])
  const ph = cols.map(() => '?').join(',')
  const stmt = dst.prepare(`INSERT INTO "${tableName}" VALUES (${ph})`)
  dst.transaction(() => rows.forEach(r => stmt.run(...cols.map(c => r[c]))))()
}

// Checkpoint backup WAL first
console.log('Checkpointing backup...')
const bak = new Database(SRC)
bak.pragma('wal_checkpoint(TRUNCATE)')
bak.close()

console.log('Source:', sizeMB(SRC), 'MB')

const src = new Database(SRC, { readonly: true })

// Remove existing dst
if (fs.existsSync(TMP)) fs.unlinkSync(TMP)

const dst = new Database(TMP)
dst.pragma('journal_mode = DELETE')
dst.pragma('foreign_keys = OFF')
dst.pragma('synchronous = OFF')

// Copy schema (exclude netfree_blocked and heuristic_blocked from sense)
const tables = src.prepare("SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND sql IS NOT NULL").all()
tables.forEach(t => {
  if (t.name === 'translation') return // drop — not needed for Torah app
  let sql = t.sql
  // Rewrite sense table to drop netfree_blocked, heuristic_blocked, etymology, cross_ref, period_tag
  // (all either 100% NULL or used only during build-time filtering)
  if (t.name === 'sense') {
    sql = sql
      .replace(/,\s*netfree_blocked[^,)]+/g, '')
      .replace(/,\s*heuristic_blocked[^,)]+/g, '')
      .replace(/,\s*etymology[^,)]+/g, '')
      .replace(/,\s*cross_ref[^,)]+/g, '')
      .replace(/,\s*period_tag[^,)]+/g, '')
  }
  // Rewrite definition table to drop filter_tag — already filtered, not needed in final DB
  if (t.name === 'definition') {
    sql = sql.replace(/,\s*filter_tag[^,)]+/g, '')
  }
  dst.exec(sql)
})

// Compute blocked senses
console.log('Computing blocked senses...')
const netfreeBlocked = new Set(src.prepare('SELECT id FROM sense WHERE netfree_blocked = 1').all().map(r => r.id))

const tagsBySense = new Map()
src.prepare('SELECT sense_id, filter_tag FROM definition WHERE filter_tag IS NOT NULL').all().forEach(r => {
  if (!tagsBySense.has(r.sense_id)) tagsBySense.set(r.sense_id, [])
  tagsBySense.get(r.sense_id).push(r.filter_tag)
})
const tagBlocked = new Set()
for (const [id, tags] of tagsBySense) {
  if (tags.some(t => BLOCKED_TAGS.has(t))) tagBlocked.add(id)
}

// Block senses tagged חדשה that have NO other sense for the same headword with a Torah period
const headwordsWithTorahSense = new Set(
  src.prepare("SELECT DISTINCT headword FROM sense WHERE period_tag IN ('מקרא', 'חז\"ל', 'ביניים')").all().map(r => r.headword)
)
const modernOnlyBlocked = new Set(
  src.prepare("SELECT id, headword FROM sense WHERE period_tag = 'חדשה'").all()
    .filter(r => !headwordsWithTorahSense.has(r.headword))
    .map(r => r.id)
)
console.log('  netfree_blocked:', netfreeBlocked.size)
console.log('  tag-blocked:', tagBlocked.size)
console.log('  חדשה-only blocked:', modernOnlyBlocked.size)

const blockedIds = new Set([...netfreeBlocked, ...tagBlocked, ...modernOnlyBlocked])
console.log('  blocked:', blockedIds.size, '/ total:', src.prepare('SELECT COUNT(*) as c FROM sense').get().c)

// Copy data
console.log('Copying...')
genericCopy(src, dst, 'source', src.prepare('SELECT * FROM source').all())

const allSenses = src.prepare('SELECT * FROM sense').all()
// Strip netfree_blocked and heuristic_blocked columns
const keptSenses = allSenses
  .filter(r => !blockedIds.has(r.id))
  .map(r => {
    const { netfree_blocked, heuristic_blocked, etymology, cross_ref, period_tag, ...rest } = r
    return rest
  })
const keptIds = new Set(keptSenses.map(r => r.id))
console.log('  kept senses:', keptSenses.length)
genericCopy(src, dst, 'sense', keptSenses)

const allDefs = src.prepare('SELECT * FROM definition').all()
const keptDefs = allDefs.filter(r => keptIds.has(r.sense_id))
  .map(r => { const { filter_tag, ...rest } = r; return rest })
const keptDefIds = new Set(keptDefs.map(r => r.id))
console.log('  kept definitions:', keptDefs.length)
genericCopy(src, dst, 'definition', keptDefs)

const allEx = src.prepare('SELECT * FROM example').all()
genericCopy(src, dst, 'example', allEx.filter(r => keptDefIds.has(r.definition_id)))

const allSec = src.prepare('SELECT * FROM section').all()
const keptSec = allSec.filter(r => keptIds.has(r.sense_id))
const keptSecIds = new Set(keptSec.map(r => r.id))
genericCopy(src, dst, 'section', keptSec)
genericCopy(src, dst, 'section_item', src.prepare('SELECT * FROM section_item').all().filter(r => keptSecIds.has(r.section_id)))
// translation table omitted — English/Arabic translations not needed for Torah app

try {
  dst.exec('CREATE TABLE IF NOT EXISTS _meta (key TEXT PRIMARY KEY, value TEXT)')
  genericCopy(src, dst, '_meta', src.prepare('SELECT * FROM _meta').all())
} catch {}

// Rebuild indexes
src.prepare("SELECT sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL").all()
  .forEach(i => { try { dst.exec(i.sql) } catch {} })

// Add ktiv_male index for search
dst.exec('CREATE INDEX IF NOT EXISTS idx_sense_ktiv_male ON sense(ktiv_male) WHERE ktiv_male IS NOT NULL')

dst.pragma('optimize')
dst.pragma('foreign_keys = ON')

const finalSenses = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
const finalDefs = dst.prepare('SELECT COUNT(*) as c FROM definition').get().c
console.log('Final: senses:', finalSenses, 'definitions:', finalDefs)

// VACUUM
console.log('Vacuuming...')
dst.exec('VACUUM')

src.close()
dst.close()

// Now swap: rename current to old, rename new to current
const OLD = DST + '.old'
if (fs.existsSync(OLD)) fs.unlinkSync(OLD)
if (fs.existsSync(DST)) fs.renameSync(DST, OLD)
fs.renameSync(TMP, DST)
if (fs.existsSync(OLD)) fs.unlinkSync(OLD)

console.log('Done:', sizeMB(DST), 'MB')
