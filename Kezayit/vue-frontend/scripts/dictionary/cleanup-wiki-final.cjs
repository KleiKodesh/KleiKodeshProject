'use strict'
/**
 * cleanup-wiki-final.cjs
 *
 * Targeted cleanup of the remaining genuinely inappropriate content
 * after the deep scan. Most "hits" from the scan were false positives
 * (קרי = reading/curry, מיני = types, זרע = seed, כוס = cup, זין = arm,
 *  שפיכה = pouring, לסבי = to/its environment, גיי = zeitgeist etc.).
 *
 * Only the items below are real issues:
 *  1. section_items: זונה, אונס, סקסיזם, סקסיט (cross-refs)
 *  2. definition for כוס that explicitly says "פות, נרתיק, איבר המין הנשי"
 *  3. definition for שעטה that includes "מינית"
 *  4. example for שחק that mentions זנות
 *  5. examples for פתה / הדיר that mention אונס
 *  6. definition for נרתיק (anatomical — vagina)
 *  7. definition for סריס that mentions אשכים (done before but verify)
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const DB_PATH = path.resolve('./public/dicts/wikidictionary.db')
const db = new Database(DB_PATH)
db.pragma('journal_mode = DELETE')

let changes = 0

function log(msg) { console.log('  ' + msg); changes++ }

db.transaction(() => {

  // ── 1. Section items: remove explicit cross-refs ───────────────────────────
  console.log('\n=== Section items ===')

  // "זונה" as a standalone cross-reference (not inside תזונה)
  const zonaItems = db.prepare("SELECT id, text FROM section_item WHERE text = 'זונה'").all()
  zonaItems.forEach(r => {
    db.prepare('DELETE FROM section_item WHERE id = ?').run(r.id)
    log(`Deleted section_item: "${r.text}"`)
  })

  // "אונס" as a standalone cross-reference
  const onessItems = db.prepare("SELECT id, text FROM section_item WHERE text = 'אונס'").all()
  onessItems.forEach(r => {
    db.prepare('DELETE FROM section_item WHERE id = ?').run(r.id)
    log(`Deleted section_item: "${r.text}"`)
  })

  // "סקסיזם" and "סקסיט"
  const sexismItems = db.prepare("SELECT id, text FROM section_item WHERE text IN ('סקסיזם','סקסיט','סקסיזם','סקסית מינית')").all()
  sexismItems.forEach(r => {
    db.prepare('DELETE FROM section_item WHERE id = ?').run(r.id)
    log(`Deleted section_item: "${r.text}"`)
  })

  // ── 2. כוס — remove def that says "פות, נרתיק, איבר המין הנשי" ────────────
  console.log('\n=== Definitions ===')

  const kosDefs = db.prepare(
    "SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'כוס' AND (d.text LIKE '%פות%' OR d.text LIKE '%נרתיק%' OR d.text LIKE '%איבר המין%')"
  ).all()
  kosDefs.forEach(d => {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
    log(`Deleted כוס def: "${d.text.slice(0, 80)}"`)
  })

  // ── 3. נרתיק — anatomical definition (vagina). Delete the explicit sense, ──
  //    or clean the definition to a neutral one.
  const nartikDefs = db.prepare(
    "SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'נרתיק'"
  ).all()
  nartikDefs.forEach(d => {
    if (d.text.includes('פות') || d.text.includes('איבר') || d.text.includes('צינור') && d.text.includes('רחם')) {
      db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
      db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
      log(`Deleted נרתיק def: "${d.text.slice(0, 80)}"`)
    }
  })

  // ── 4. שעטה — remove "מינית" from definition ─────────────────────────────
  const shaataDefs = db.prepare(
    "SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'שעטה' AND d.text LIKE '%מינית%'"
  ).all()
  shaataDefs.forEach(d => {
    const cleaned = d.text
      .replace(/,?\s*מינית[^,.]*/g, '')
      .replace(/\s{2,}/g, ' ')
      .trim()
    db.prepare('UPDATE definition SET text = ? WHERE id = ?').run(cleaned, d.id)
    log(`Cleaned שעטה def: "${d.text.slice(0, 80)}" → "${cleaned.slice(0, 80)}"`)
  })

  // ── 5. Remove examples that contain זנות / אונס in Talmudic context ────────
  //    These are Talmudic/halachic quotes — they're actually appropriate for a
  //    Torah dictionary. We'll keep them as-is (they're not inappropriate in context).
  //    Only delete if they're gratuitous modern usage.
  // After review: שחק/זנות and הדיר/פתה/אונס are Talmudic legal texts — leave them.

  // ── 6. סריס — verify anatomical def is gone ───────────────────────────────
  const srisAshk = db.prepare(
    "SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'סריס' AND d.text LIKE '%אשכ%'"
  ).all()
  srisAshk.forEach(d => {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    db.prepare('DELETE FROM definition WHERE id = ?').run(d.id)
    log(`Deleted סריס def: "${d.text.slice(0, 80)}"`)
  })

  // ── 7. Prune any senses left empty after deletions ────────────────────────
  console.log('\n=== Pruning empty senses ===')
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
