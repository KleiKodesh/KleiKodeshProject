'use strict'
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

let deleted = 0, cleaned = 0

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

function deleteSenseById(senseId, label) {
  const defs = db.prepare('SELECT id FROM definition WHERE sense_id = ?').all(senseId)
  defs.forEach(d => db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id))
  db.prepare('DELETE FROM definition WHERE sense_id = ?').run(senseId)
  const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(senseId)
  secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
  db.prepare('DELETE FROM section WHERE sense_id = ?').run(senseId)
  db.prepare('DELETE FROM sense WHERE id = ?').run(senseId)
  console.log(`  DELETED sense: ${label}`); deleted++
}

function deleteDefContaining(headword, fragment, label) {
  const defs = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ?").all(headword, `%${fragment}%`)
  defs.forEach(d => {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
  })
  if (defs.length > 0) { console.log(`  DELETED def: ${label}`); cleaned++ }
}

function updateDef(headword, fragment, newText, label) {
  const def = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ?").get(headword, `%${fragment}%`)
  if (def) { db.prepare('UPDATE definition SET text = ? WHERE id = ?').run(newText, def.id); console.log(`  UPDATED: ${label}`); cleaned++ }
}

db.transaction(() => {

  console.log('\n=== DELETE entire headwords ===')

  // Ideology/worldview against Orthodox Judaism
  const deleteList = [
    'ציונות', 'ציוני',           // Zionism — against Orthodox beliefs
    'אתאיזם', 'אתאיסט',          // Atheism
    'פמיניזם',                    // Feminism
    'ניהיליזם',                   // Nihilism
    'אגנוסטי',                    // Agnostic
    // Contraception
    'התקן תוך רחמי', 'מירנה',
    // Drugs
    'אופיום', 'קודאין',
    // Smoking
    'סיגריה', 'סיגריה אלקטרונית', 'ניקוטין',
    // Gambling
    'קזינו',
    // Medical/inappropriate
    'גיל ההתבגרות', 'הנקה',
    'אנורקסיה',
    'אקסהיביציוניזם',
    'אנקופרזיס', 'השתלת צואה',
    // Profanity
    'פאק',
  ]
  deleteList.forEach(deleteSense)

  // פוקר sense 1 (card game/gambling) — keep sense 0 (heretic/פקר)
  const pokerSense1 = db.prepare("SELECT id FROM sense WHERE headword = 'פוקר' AND sense_order = 1").get()
  if (pokerSense1) deleteSenseById(pokerSense1.id, 'פוקר sense 1 (gambling)')

  console.log('\n=== UPDATE definitions ===')

  // וסת — remove the menstrual definition, keep "הרגל/מנהג" and "ויסות"
  deleteDefContaining('וסת', 'יציאת דם', 'וסת (menstrual def)')
  deleteDefContaining('וסת', 'ביוץ', 'וסת (ovulation def)')
  deleteDefContaining('וסת', 'דם הנדה', 'וסת (nidah def)')

  // מחזור — remove the menstrual definition, keep all others
  deleteDefContaining('מחזור', 'יציאת הביצית', 'מחזור (menstrual def)')
  deleteDefContaining('מחזור', 'תהליך יציאת', 'מחזור (menstrual def2)')

  // Add דם נידה as a new headword
  const nidahExists = db.prepare("SELECT id FROM sense WHERE headword = 'דם נידה'").get()
  if (!nidahExists) {
    const sourceId = db.prepare("SELECT id FROM source WHERE label = 'ויקימילון'").get()?.id ?? 1
    const maxSenseId = db.prepare('SELECT MAX(id) as m FROM sense').get().m + 1
    db.prepare('INSERT INTO sense (id, headword, nikud, pos, binyan, shoresh, ktiv_male, source_id, sense_order) VALUES (?,?,?,?,?,?,?,?,?)').run(
      maxSenseId, 'דם נידה', null, 'שם עצם', null, null, null, sourceId, 0
    )
    const maxDefId = db.prepare('SELECT MAX(id) as m FROM definition').get().m + 1
    db.prepare('INSERT INTO definition (id, sense_id, text, def_order) VALUES (?,?,?,?)').run(
      maxDefId, maxSenseId, 'דם הנדה — דם הטומאה היוצא מגוף האישה בעת הווסת, הגורם לאיסור נידה לפי ההלכה.', 0
    )
    console.log('  ADDED: דם נידה')
    cleaned++
  }

  // גלולה — remove contraceptive definition if present, keep "תרופה עגולה"
  // Already clean — "תרופה עגולה ומוצקה לבליעה" is fine

  // אבולוציוני — delete (references evolution)
  deleteSense('אבולוציוני')

  // Also delete any sense of ציוני that remains
  // (already handled above)

  // Clean section items referencing deleted concepts
  console.log('\n=== Clean section items ===')
  const badTerms = ['ציונות','ציוני','אתאיזם','פמיניזם','אבולוציה','קזינו','סיגריה','מריחואנה']
  badTerms.forEach(term => {
    const items = db.prepare('SELECT id FROM section_item WHERE text LIKE ?').all(`%${term}%`)
    items.forEach(i => db.prepare('DELETE FROM section_item WHERE id = ?').run(i.id))
    if (items.length > 0) { console.log(`  Removed ${items.length} section items: "${term}"`); cleaned++ }
  })

  // Also remove from definitions any reference to ציונות/אבולוציה in innocent words
  // (these are just cross-references, not the main definition)

})()

console.log(`\nDone: ${deleted} deleted, ${cleaned} cleaned`)
db.exec('VACUUM')
console.log('Size:', Math.round(fs.statSync('./public/dicts/wikidictionary.db').size/1024/1024*10)/10, 'MB')
db.close()
