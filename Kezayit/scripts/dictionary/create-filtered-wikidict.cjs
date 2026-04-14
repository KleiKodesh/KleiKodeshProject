'use strict'
/**
 * create-filtered-wikidict.cjs
 *
 * Creates a filtered wikidictionary.db keeping only content strictly suitable
 * for a Torah Hebrew book reader. Moves the original to data/dictionaries/.
 *
 * Filtering policy:
 * - Exclude any headword flagged netfree_blocked = 1
 * - Exclude any sense where ANY definition has a blocked filter_tag
 */

const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const SRC = path.resolve(__dirname, '../../public/dicts/wikidictionary.db')
const DST = path.resolve(__dirname, '../../public/dicts/filtered_wikidictionary.db')
const BACKUP_DIR = path.resolve(__dirname, '../../data/dictionaries')

const BLOCKED_TAGS = new Set([
  'סלנג', 'סלנג ישן', 'סלנג ירוד', 'סלנג ישיבות', 'סלנג, צה"ל',
  'סלנג להט"בי',
  'עממי', 'כינוי גנאי', 'משלב חסר', 'שפת הדיבור', 'לשון מדוברת', 'דיבור',
  'עגה צה"לית', 'עגה ירושלמית', 'צה"ל', 'צבא',
  'נצרות', 'מיתולוגיה', 'אידאולוגיות',
])

function genericCopy(src, dst, tableName, rows) {
  if (!rows.length) return
  const cols = Object.keys(rows[0])
  const ph = cols.map(() => '?').join(',')
  const stmt = dst.prepare(`INSERT INTO "${tableName}" VALUES (${ph})`)
  dst.transaction(() => rows.forEach(r => stmt.run(...cols.map(c => r[c]))))()
}

function main() {
  if (!fs.existsSync(SRC)) { console.error('Source DB not found:', SRC); process.exit(1) }
  if (fs.existsSync(DST)) fs.unlinkSync(DST)

  console.log('Opening source DB...')
  const src = new Database(SRC, { readonly: true })

  console.log('Creating filtered DB...')
  const dst = new Database(DST)
  dst.pragma('journal_mode = WAL')
  dst.pragma('foreign_keys = OFF')
  dst.pragma('synchronous = OFF')

  // Copy schema
  const tables = src.prepare("SELECT sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND sql IS NOT NULL").all()
  tables.forEach(t => dst.exec(t.sql))

  // Compute blocked sense ids
  console.log('Computing blocked senses...')
  const netfreeBlocked = new Set(src.prepare('SELECT id FROM sense WHERE netfree_blocked = 1').all().map(r => r.id))
  console.log('  netfree_blocked:', netfreeBlocked.size)

  // Build map: sense_id -> array of filter_tags
  const tagsBySense = new Map()
  src.prepare('SELECT sense_id, filter_tag FROM definition WHERE filter_tag IS NOT NULL').all().forEach(r => {
    if (!tagsBySense.has(r.sense_id)) tagsBySense.set(r.sense_id, [])
    tagsBySense.get(r.sense_id).push(r.filter_tag)
  })
  const tagBlocked = new Set()
  for (const [senseId, tags] of tagsBySense) {
    if (tags.some(t => BLOCKED_TAGS.has(t))) tagBlocked.add(senseId)
  }
  console.log('  tag-blocked:', tagBlocked.size)

  const blockedSenseIds = new Set([...netfreeBlocked, ...tagBlocked])
  console.log('  total blocked:', blockedSenseIds.size)

  // Copy tables
  console.log('Copying data...')

  genericCopy(src, dst, 'source', src.prepare('SELECT * FROM source').all())

  const allSenses = src.prepare('SELECT * FROM sense').all()
  const keptSenses = allSenses.filter(r => !blockedSenseIds.has(r.id))
  const keptSenseIds = new Set(keptSenses.map(r => r.id))
  console.log('  senses:', keptSenses.length, '/', allSenses.length)
  genericCopy(src, dst, 'sense', keptSenses)

  const allDefs = src.prepare('SELECT * FROM definition').all()
  const keptDefs = allDefs.filter(r => keptSenseIds.has(r.sense_id))
  const keptDefIds = new Set(keptDefs.map(r => r.id))
  console.log('  definitions:', keptDefs.length, '/', allDefs.length)
  genericCopy(src, dst, 'definition', keptDefs)

  const allEx = src.prepare('SELECT * FROM example').all()
  genericCopy(src, dst, 'example', allEx.filter(r => keptDefIds.has(r.definition_id)))

  const allSec = src.prepare('SELECT * FROM section').all()
  const keptSec = allSec.filter(r => keptSenseIds.has(r.sense_id))
  const keptSecIds = new Set(keptSec.map(r => r.id))
  genericCopy(src, dst, 'section', keptSec)

  const allSecItems = src.prepare('SELECT * FROM section_item').all()
  genericCopy(src, dst, 'section_item', allSecItems.filter(r => keptSecIds.has(r.section_id)))

  const allTrans = src.prepare('SELECT * FROM translation').all()
  genericCopy(src, dst, 'translation', allTrans.filter(r => keptSenseIds.has(r.sense_id)))

  try {
    dst.exec('CREATE TABLE IF NOT EXISTS _meta (key TEXT PRIMARY KEY, value TEXT)')
    genericCopy(src, dst, '_meta', src.prepare('SELECT * FROM _meta').all())
  } catch {}

  // Rebuild indexes
  console.log('Building indexes...')
  src.prepare("SELECT sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL").all()
    .forEach(i => { try { dst.exec(i.sql) } catch {} })

  dst.pragma('optimize')
  dst.pragma('foreign_keys = ON')

  const finalHeadwords = dst.prepare('SELECT COUNT(DISTINCT headword) as c FROM sense').get().c
  console.log('Filtered DB headwords:', finalHeadwords, '(was', src.prepare('SELECT COUNT(DISTINCT headword) as c FROM sense').get().c, ')')

  src.close()
  dst.close()

  // Move original to backup
  console.log('Backing up original to', BACKUP_DIR)
  fs.mkdirSync(BACKUP_DIR, { recursive: true })
  ;[SRC, SRC + '-wal', SRC + '-shm'].forEach(f => {
    if (fs.existsSync(f)) fs.renameSync(f, path.join(BACKUP_DIR, path.basename(f)))
  })

  // Rename filtered → wikidictionary.db
  fs.renameSync(DST, SRC)
  console.log('Done. New wikidictionary.db is in public/dicts/')
  console.log('Original backed up to data/dictionaries/')
}

main()
