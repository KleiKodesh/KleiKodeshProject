'use strict'
/**
 * cleanup-wiki-multiword-and-inappropriate.cjs
 *
 * 1. Removes all multi-word headwords from wikidictionary.db
 *    (entries with a space in the headword — phrases, not single words)
 * 2. Removes remaining inappropriate definitions/senses that slipped
 *    through the previous cleanup pass.
 *
 * Run: node scripts/dictionary/cleanup-wiki-multiword-and-inappropriate.cjs
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const DB_PATH = path.resolve('./public/dicts/wikidictionary.db')
const db = new Database(DB_PATH)
db.pragma('journal_mode = DELETE')

let deletedSenses = 0
let deletedDefs = 0

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Fully delete a sense and all its child rows */
function deleteSenseById(senseId) {
  const defs = db.prepare('SELECT id FROM definition WHERE sense_id = ?').all(senseId)
  defs.forEach((d) => db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id))
  db.prepare('DELETE FROM definition WHERE sense_id = ?').run(senseId)
  const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(senseId)
  secs.forEach((sec) => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
  db.prepare('DELETE FROM section WHERE sense_id = ?').run(senseId)
  db.prepare('DELETE FROM sense WHERE id = ?').run(senseId)
  deletedSenses++
}

/** Delete a definition row and its examples */
function deleteDefById(defId, label) {
  db.prepare('DELETE FROM example WHERE definition_id = ?').run(defId)
  db.prepare('DELETE FROM definition WHERE id = ?').run(defId)
  console.log(`  DELETED def: ${label}`)
  deletedDefs++
}

// ── Run everything in one transaction ────────────────────────────────────────

db.transaction(() => {

  // ── 1. Delete all multi-word headwords ─────────────────────────────────────
  console.log('\n=== 1. Deleting multi-word headwords ===')
  const multiWordSenses = db
    .prepare("SELECT id, headword FROM sense WHERE headword LIKE '% %'")
    .all()

  const seen = new Set()
  for (const s of multiWordSenses) {
    deleteSenseById(s.id)
    if (!seen.has(s.headword)) {
      seen.add(s.headword)
    }
  }
  console.log(`  Deleted ${deletedSenses} senses for ${seen.size} multi-word headwords`)

  // ── 2. Delete specific inappropriate single-word entries ───────────────────
  console.log('\n=== 2. Deleting inappropriate single-word headwords ===')

  // אנס מתקן — "corrective rape" entry (already multi-word, caught above, but guard anyway)
  // סריס — anatomical definition mentioning אשכים
  const srisDefsWithAshk = db
    .prepare(
      "SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'סריס' AND d.text LIKE '%אשכ%'",
    )
    .all()
  srisDefsWithAshk.forEach((d) => deleteDefById(d.id, 'סריס (anatomical אשכים)'))

  // ── 3. Delete definitions containing genuinely inappropriate terms ──────────
  // These are exact-match checks — we only delete when the term is clearly
  // used in an inappropriate context, not as a substring of an unrelated word.
  console.log('\n=== 3. Cleaning inappropriate definitions ===')

  // Terms that are unambiguously inappropriate in any definition context
  const hardBadTerms = [
    'ארוטי',      // erotic
    'ארוטיקה',    // erotica
    'יחסי מין',   // sexual relations
    'יחסי אישות', // marital relations (explicit)
    'איבר מין',   // genitalia
    'אוננ',       // masturbation
    'פורנוגרפ',   // pornography
    'אורגזמ',     // orgasm
    'ליבידו',     // libido
    'פטיש מיני',  // sexual fetish
    'בדסמ',       // BDSM
  ]

  for (const term of hardBadTerms) {
    const rows = db
      .prepare(
        'SELECT d.id, s.headword, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE d.text LIKE ?',
      )
      .all(`%${term}%`)
    for (const row of rows) {
      deleteDefById(row.id, `${row.headword} (contains "${term}"): ${row.text.slice(0, 60)}`)
    }
  }

  // ── 4. Delete senses that now have zero definitions ─────────────────────────
  console.log('\n=== 4. Pruning senses with no remaining definitions ===')
  const emptySenses = db
    .prepare(
      'SELECT s.id, s.headword FROM sense s WHERE NOT EXISTS (SELECT 1 FROM definition d WHERE d.sense_id = s.id)',
    )
    .all()
  for (const s of emptySenses) {
    deleteSenseById(s.id)
    console.log(`  Pruned empty sense: ${s.headword}`)
  }

  // ── 5. Clean section_items containing inappropriate cross-references ────────
  console.log('\n=== 5. Cleaning inappropriate section items ===')
  const badSectionTerms = [
    'ארוטי', 'ארוטיקה', 'סקסואל', 'הומוסקסו', 'יחסי מין',
    'זנות', 'פורנ', 'אוננ', 'ביסקסו', 'לסבי',
  ]
  for (const term of badSectionTerms) {
    const items = db.prepare('SELECT id FROM section_item WHERE text LIKE ?').all(`%${term}%`)
    items.forEach((i) => db.prepare('DELETE FROM section_item WHERE id = ?').run(i.id))
    if (items.length > 0) console.log(`  Removed ${items.length} section items containing "${term}"`)
  }

})()

console.log(`\n=== Done ===`)
console.log(`  Senses deleted: ${deletedSenses}`)
console.log(`  Definitions deleted/cleaned: ${deletedDefs}`)

// Reclaim space
console.log('\nRunning VACUUM...')
db.exec('VACUUM')
db.close()

const sizeMB = (fs.statSync(DB_PATH).size / 1024 / 1024).toFixed(1)
console.log(`Final DB size: ${sizeMB} MB`)
