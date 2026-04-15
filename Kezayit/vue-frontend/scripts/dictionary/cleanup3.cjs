'use strict'
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

let deleted = 0

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
  if (senses.length > 0) { console.log(`  DELETED: ${headword}`); deleted++ }
}

function deleteDefContaining(headword, fragment) {
  const defs = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ?").all(headword, `%${fragment}%`)
  defs.forEach(d => {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
  })
  if (defs.length > 0) console.log(`  CLEANED def: ${headword} (${fragment.substring(0,20)})`)
}

db.transaction(() => {

  // ── Political ideologies (no Torah source) ────────────────────────────────
  const political = [
    'קומוניזם', 'קומוניסט', 'בולשביק', 'בנק"י', 'ברית המועצות', 'מק"י', 'רק"ח',
    'סוציאליזם', 'סוציאליזם חזירי', 'שמאל', 'שמאלן',
    'קפיטליזם', 'קפיטליזם חזירי', 'קפיטליסט',
    'דמוקרטיה', 'דמוקרטיה יצוגית', 'דמוקרטיה ישירה', 'דמוקרטיה ליברלית',
    'אנרכיזם', 'פשיזם', 'ליברליזם',
    'ימין', 'שמאל', // political left/right
  ]
  political.forEach(deleteSense)

  // ── Modern entertainment/media ─────────────────────────────────────────────
  const entertainment = [
    'קולנוע', 'טלוויזיה', 'רדיו', // already not in DB but check
    'ספורט', 'כדורגל', 'כדורסל', 'טניס',
    'אופרה', 'תיאטרון', 'דיסקו',
    'אופנה', 'איפור', 'מניקור', 'פדיקור',
    'בינה מלאכותית', 'רובוט', 'רובוטיקה',
    'רשת חברתית',
  ]
  entertainment.forEach(deleteSense)

  // ── Medical/psychological (modern, no Torah source) ───────────────────────
  const medical = [
    'הפרעת אכילה', 'סכיזופרניה',
    'גרידה', // abortion procedure
    'מדיטציה', 'יוגה',
    'אסטרולוגיה', 'הורוסקופ',
    'לובסטר', // non-kosher, modern word
  ]
  medical.forEach(deleteSense)

  // ── Clean specific definitions ─────────────────────────────────────────────
  // בן זוג — remove romantic/sexual sense, keep halachic partner sense
  deleteDefContaining('בן זוג', 'קשר רומנטי')
  deleteDefContaining('בן זוג', 'פעילות זוגית')

  // הפלה — keep only the Torah sense (הפלה = differentiation/distinction)
  // The abortion sense needs to go
  deleteDefContaining('הפלה', 'הפסקת הריון')
  deleteDefContaining('הפלה', 'הפלה מלאכותית')

  // חרדה — keep the word (has Torah source שמואל א) but remove modern psychiatric definitions
  deleteDefContaining('חרדה', 'הפרעת חרדה')
  deleteDefContaining('חרדה', 'פסיכולוגי')

  // מחשב — keep as "calculator/computing device" but remove modern tech definitions
  // Actually מחשב is a legitimate modern Hebrew word derived from חשב — keep

  // Clean section items with modern terms
  const modernTerms = [
    'קומוניזם', 'סוציאליזם', 'קפיטליזם', 'דמוקרטיה', 'פשיזם',
    'ספורט', 'כדורגל', 'טלוויזיה', 'קולנוע', 'אופרה',
    'יוגה', 'מדיטציה', 'אסטרולוגיה',
  ]
  modernTerms.forEach(term => {
    const items = db.prepare('SELECT id FROM section_item WHERE text LIKE ?').all(`%${term}%`)
    items.forEach(i => db.prepare('DELETE FROM section_item WHERE id = ?').run(i.id))
    if (items.length > 0) console.log(`  Removed ${items.length} section items: "${term}"`)
  })

})()

console.log(`\nDone: ${deleted} headwords deleted`)
db.exec('VACUUM')
console.log('Size:', Math.round(fs.statSync('./public/dicts/wikidictionary.db').size/1024/1024*10)/10, 'MB')
db.close()
