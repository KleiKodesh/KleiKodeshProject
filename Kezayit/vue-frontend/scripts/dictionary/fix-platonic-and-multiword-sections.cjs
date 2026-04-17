'use strict'
/**
 * fix-platonic-and-multiword-sections.cjs
 *
 * The previous cleanup removed multi-word headwords from the `sense` table,
 * but multi-word phrases also survive as cross-reference links inside
 * `section_item`. This script:
 *  1. Shows what section_item entries contain multi-word text (diagnosis)
 *  2. Deletes section_items that are multi-word phrases
 *  3. Deletes section_items containing inappropriate terms
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const DB_PATH = path.resolve('./public/dicts/wikidictionary.db')
const db = new Database(DB_PATH)
db.pragma('journal_mode = DELETE')

// ── Diagnosis first ───────────────────────────────────────────────────────────
console.log('\n=== SECTION ITEMS WITH SPACES (multi-word cross-refs) ===')
const multiWordItems = db.prepare("SELECT si.id, si.text, s.headword as parent FROM section_item si JOIN section sec ON sec.id = si.section_id JOIN sense s ON s.id = sec.sense_id WHERE si.text LIKE '% %' LIMIT 50").all()
multiWordItems.forEach(r => console.log(`  [${r.parent}] → "${r.text}"`))
console.log(`  Total shown: ${multiWordItems.length} (capped at 50)`)

const totalMulti = db.prepare("SELECT COUNT(*) as c FROM section_item WHERE text LIKE '% %'").get()
console.log(`  Total multi-word section items: ${totalMulti.c}`)

// ── Inappropriate terms still in section_items ────────────────────────────────
console.log('\n=== INAPPROPRIATE SECTION ITEMS ===')
const badTerms = ['ארוטי', 'ארוטיקה', 'סקסואל', 'הומוסקסו', 'יחסי מין', 'זנות', 'פורנ', 'אוננ', 'ביסקסו', 'לסביות', 'לסבית']
badTerms.forEach(term => {
  const rows = db.prepare('SELECT id, text FROM section_item WHERE text LIKE ?').all(`%${term}%`)
  if (rows.length) {
    console.log(`[${term}] ${rows.length} items:`)
    rows.forEach(r => console.log(`  "${r.text}"`))
  }
})

// ── Execute cleanup ───────────────────────────────────────────────────────────
let deleted = 0

db.transaction(() => {
  // Delete all multi-word section_items
  const result = db.prepare("DELETE FROM section_item WHERE text LIKE '% %'").run()
  deleted += result.changes
  console.log(`\nDeleted ${result.changes} multi-word section items`)

  // Delete inappropriate single-word section_items
  const extraBadTerms = ['ארוטי', 'ארוטיקה', 'סקסואל', 'הומוסקסו', 'יחסי מין', 'זנות', 'פורנוגרפ', 'אוננ', 'ביסקסו', 'לסביות']
  for (const term of extraBadTerms) {
    const r = db.prepare('DELETE FROM section_item WHERE text LIKE ?').run(`%${term}%`)
    if (r.changes > 0) {
      console.log(`Deleted ${r.changes} section items containing "${term}"`)
      deleted += r.changes
    }
  }
})()

console.log(`\n=== Done: ${deleted} section items deleted ===`)

db.exec('VACUUM')
db.close()

const sizeMB = (fs.statSync(DB_PATH).size / 1024 / 1024).toFixed(1)
console.log(`Final DB size: ${sizeMB} MB`)
