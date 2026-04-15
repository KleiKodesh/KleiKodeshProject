'use strict'
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')
let deleted = 0

function del(headword) {
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
  if (senses.length > 0) { console.log('  DELETED: ' + headword); deleted++ }
}

db.transaction(() => {
  // Anatomy/medical (inappropriate)
  del('גינקומסטיה')  // male breast development
  del('פטמה')        // nipple
  del('עכבר השד')    // breast lump
  del('פיברואדנומה') // breast tumor
  del('אנדוקרינולוג')
  del('אנדוקרינולוגיה')
  del('אדרנל')
  del('בלוטת הטוחה')
  del('בלוטת יותרת הכליה')
  del('הורמון גדילה')
  del('אנדודרמה')
  del('אקנה')

  // Drugs
  del("צ'ילום")

  // Delete empty senses
  const emptySenses = db.prepare('SELECT s.id FROM sense s WHERE NOT EXISTS (SELECT 1 FROM definition d WHERE d.sense_id = s.id)').all()
  emptySenses.forEach(s => {
    const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(s.id)
    secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
    db.prepare('DELETE FROM section WHERE sense_id = ?').run(s.id)
    db.prepare('DELETE FROM sense WHERE id = ?').run(s.id)
    deleted++
  })
  if (emptySenses.length > 0) console.log('  Deleted empty senses')
})()

console.log('\nDone: ' + deleted + ' deleted')
db.exec('VACUUM')
console.log('Size: ' + Math.round(fs.statSync('./public/dicts/wikidictionary.db').size/1024/1024*10)/10 + ' MB')
db.close()
