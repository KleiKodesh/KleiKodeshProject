'use strict'
/**
 * cleanup-wiki-pass3.cjs
 * Third cleanup pass — handles the real remaining issues from the full audit.
 * Everything else in the audit was a false positive.
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const DB_PATH = path.resolve('./public/dicts/wikidictionary.db')
const db = new Database(DB_PATH)
db.pragma('journal_mode = DELETE')

let changes = 0

function log(msg) { console.log('  ' + msg); changes++ }

function deleteSense(headword) {
  const senses = db.prepare('SELECT id FROM sense WHERE headword = ?').all(headword)
  senses.forEach(s => {
    const defs = db.prepare('SELECT id FROM definition WHERE sense_id = ?').all(s.id)
    defs.forEach(d => db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id))
    db.prepare('DELETE FROM definition WHERE sense_id = ?').run(s.id)
    const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(s.id)
    secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
    db.prepare('DELETE FROM section WHERE sense_id = ?').run(s.id)
    db.prepare('DELETE FROM sense WHERE id = ?').run(s.id)
  })
  if (senses.length) log(`Deleted headword: ${headword} (${senses.length} senses)`)
}

function deleteDefContaining(headword, fragment) {
  const rows = db.prepare(
    'SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ?'
  ).all(headword, `%${fragment}%`)
  rows.forEach(d => {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
    log(`Deleted def [${headword}]: "${d.text.slice(0, 90)}"`)
  })
}

function updateDef(headword, fragment, newText) {
  const row = db.prepare(
    'SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ? LIMIT 1'
  ).get(headword, `%${fragment}%`)
  if (row) {
    db.prepare('UPDATE definition SET text = ? WHERE id = ?').run(newText, row.id)
    log(`Updated def [${headword}]: "${newText.slice(0, 90)}"`)
  }
}

db.transaction(() => {

  // ── 1. כושי — racial slur headword ───────────────────────────────────────
  // The word appears as a biblical/geographic term (כוש = land of Cush) in
  // other entries, but as a standalone headword it is a slur. Delete it.
  console.log('\n=== 1. Racial slur headword ===')
  deleteSense('כושי')

  // ── 2. דקולטה — exposed cleavage definition ──────────────────────────────
  // This is borderline but acceptable as a fashion/clothing term (it's used
  // in modesty laws discussions). Keep it — it's a neutral garment term.

  // ── 3. פוליאנדריה — woman married to multiple men ─────────────────────────
  // This is a halachic/anthropological term. Keep it — it's referenced in
  // discussions of עגינות and Jewish law. No action needed.

  // ── 4. מיננות — sexism/gender discrimination ─────────────────────────────
  // Legitimate sociology term. Keep it.

  // ── 5. נרקומן — clean slightly but keep core meaning ─────────────────────
  // "מי שמכור לסמים. שהורגל להתנתק מקשיים בהשפעתם, שגופו דורש
  //  חומרים נרקוטים ומגיב בצורה קשה למניעתם."
  // This is a factual dictionary definition. Keep it as-is.

  // ── 6. Remove section_items cross-linking to slurs/explicit ──────────────
  console.log('\n=== 2. Section items cleanup ===')
  // Any remaining references to words we deleted
  const deleteTerms = ['כושי', 'זונה', 'אונס', 'הומוסקסואל', 'לסבית', 'ביסקסואל']
  for (const term of deleteTerms) {
    const rows = db.prepare("SELECT id FROM section_item WHERE text = ?").all(term)
    rows.forEach(r => {
      db.prepare('DELETE FROM section_item WHERE id = ?').run(r.id)
      log(`Deleted section_item: "${term}"`)
    })
  }

  // ── 7. וודו definition — contains "כושיים" (racial) ──────────────────────
  console.log('\n=== 3. Definitions with racial language ===')
  // "אמונתם ומעשי הכשפים של אי אלו שבטים כושיים באפריקה."
  // Replace with neutral phrasing
  updateDef(
    'וודו',
    'כושיים',
    'אמונה עממית ומנהגי פולחן של שבטים מסוימים באפריקה ובקהילות המהגרים שלהם.'
  )

  // ── 8. Prune empty senses ─────────────────────────────────────────────────
  console.log('\n=== 4. Prune empty senses ===')
  const emptySenses = db.prepare(
    'SELECT s.id, s.headword FROM sense s WHERE NOT EXISTS (SELECT 1 FROM definition d WHERE d.sense_id = s.id)'
  ).all()
  emptySenses.forEach(s => {
    const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(s.id)
    secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
    db.prepare('DELETE FROM section WHERE sense_id = ?').run(s.id)
    db.prepare('DELETE FROM sense WHERE id = ?').run(s.id)
    log(`Pruned empty sense: "${s.headword}"`)
  })

})()

console.log(`\n=== Done: ${changes} changes ===`)
db.exec('VACUUM')
db.close()
const sizeMB = (fs.statSync(DB_PATH).size / 1024 / 1024).toFixed(1)
console.log(`Final DB size: ${sizeMB} MB`)
