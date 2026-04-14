'use strict'
/**
 * cleanup-inappropriate.cjs
 *
 * Comprehensive cleanup of inappropriate content from wikidictionary.db.
 * Based on thorough analysis and user decisions.
 */
const Database = require('better-sqlite3')
const path = require('path')
const fs = require('fs')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

let deleted = 0, cleaned = 0

// ── Helpers ───────────────────────────────────────────────────────────────────

function deleteSense(headword) {
  const senses = db.prepare('SELECT id FROM sense WHERE headword = ?').all(headword)
  senses.forEach(s => {
    const defs = db.prepare('SELECT id FROM definition WHERE sense_id = ?').all(s.id)
    defs.forEach(d => {
      db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id)
    })
    db.prepare('DELETE FROM definition WHERE sense_id = ?').run(s.id)
    const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(s.id)
    secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
    db.prepare('DELETE FROM section WHERE sense_id = ?').run(s.id)
    db.prepare('DELETE FROM sense WHERE id = ?').run(s.id)
  })
  if (senses.length > 0) { console.log(`  DELETED headword: ${headword} (${senses.length} senses)`); deleted++ }
}

function deleteSenseById(senseId, label) {
  const defs = db.prepare('SELECT id FROM definition WHERE sense_id = ?').all(senseId)
  defs.forEach(d => db.prepare('DELETE FROM example WHERE definition_id = ?').run(d.id))
  db.prepare('DELETE FROM definition WHERE sense_id = ?').run(senseId)
  const secs = db.prepare('SELECT id FROM section WHERE sense_id = ?').all(senseId)
  secs.forEach(sec => db.prepare('DELETE FROM section_item WHERE section_id = ?').run(sec.id))
  db.prepare('DELETE FROM section WHERE sense_id = ?').run(senseId)
  db.prepare('DELETE FROM sense WHERE id = ?').run(senseId)
  console.log(`  DELETED sense: ${label}`)
  deleted++
}

function deleteDefinition(defId, label) {
  db.prepare('DELETE FROM example WHERE definition_id = ?').run(defId)
  db.prepare('DELETE FROM definition WHERE id = ?').run(defId)
  console.log(`  DELETED def: ${label}`)
  cleaned++
}

function updateDefinition(defId, newText, label) {
  db.prepare('UPDATE definition SET text = ? WHERE id = ?').run(newText, defId)
  console.log(`  UPDATED def: ${label} -> "${newText}"`)
  cleaned++
}

function getDefId(headword, textFragment) {
  const r = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = ? AND d.text LIKE ?").get(headword, `%${textFragment}%`)
  return r?.id
}

function getSenseId(headword, senseOrder) {
  const r = db.prepare('SELECT id FROM sense WHERE headword = ? AND sense_order = ?').get(headword, senseOrder)
  return r?.id
}

db.transaction(() => {

  console.log('\n=== 1. DELETE entire headwords (purely inappropriate) ===')
  const toDeleteEntirely = [
    // Sexual/explicit
    'אורופיליה', 'מקלחת זהב', 'טרמפלינג', 'נקדת ג\'י', 'ספיוסקסואל',
    'אאוטינג', 'הטרופוביה', 'טפול המרה', 'טרנסקסואל', 'טרנסקסואליות',
    'יצא מן הארון', 'סטריפטיז', 'אפרודיטי', 'ספרמטוגנזה', 'צנור הזרע',
    'שכבת זרע', 'פימוזיס', 'כרות שפכה', 'אנס סטטוטורי', 'אנס קבוצתי',
    'פדרסט', 'פוצי מוצי', 'פרגע', 'נשואים פתוחים', 'הבעיל',
    'דש מבפנים וזורה מבחוץ', 'התעלס', 'מחלת מין', 'עגבת', 'זיבה',
    'בלוטת הערמונית', 'וריקוצלה', 'מגן ביצים', 'טנגה',
    // Drugs
    'ג\'וינט', 'מריחואנה', 'קוקאין', 'אמפטמין', 'מת\'', 'איבוגאין',
    'קססה',
    // Modern/inappropriate
    'תחתונים', 'חדר מטות', 'נשואים פתוחים',
    'שפיכה', 'אביונה', // sense 0 only — handled below
  ]
  // Actually handle אביונה and שפיכה separately
  const deleteSimple = toDeleteEntirely.filter(w => !['אביונה', 'שפיכה'].includes(w))
  deleteSimple.forEach(deleteSense)

  console.log('\n=== 2. DELETE specific senses ===')

  // ארוס sense 1 (sexual desire, Greek god, Freud) — keep sense 0 (betrothed)
  const arosSense1 = getSenseId('ארוס', 1)
  if (arosSense1) deleteSenseById(arosSense1, 'ארוס sense 1 (sexual/Greek)')

  // דו מיני sense with bisexual meaning — keep biological hermaphrodite sense
  const duMiniSenses = db.prepare("SELECT id, sense_order FROM sense WHERE headword = 'דו מיני'").all()
  duMiniSenses.forEach(s => {
    const defs = db.prepare('SELECT text FROM definition WHERE sense_id = ?').all(s.id)
    const hasBisexual = defs.some(d => d.text.includes('ביסקסואל') || d.text.includes('נמשך'))
    if (hasBisexual) deleteSenseById(s.id, 'דו מיני (bisexual sense)')
  })

  // שפיכה — delete entirely (only inappropriate definitions)
  deleteSense('שפיכה')

  // אביונה sense 0 (sexual) — delete, keep sense 1 (plant/poor woman)
  const abiyonaSense0 = getSenseId('אביונה', 0)
  if (abiyonaSense0) deleteSenseById(abiyonaSense0, 'אביונה sense 0 (sexual)')

  // קרה sense 3 (seminal emission) — delete
  const karahSense3 = getSenseId('קרה', 3)
  if (karahSense3) deleteSenseById(karahSense3, 'קרה sense 3 (emission)')

  // רבע sense 0 (sexual) — delete
  const rova0 = getSenseId('רבע', 0)
  if (rova0) deleteSenseById(rova0, 'רבע sense 0 (sexual)')

  console.log('\n=== 3. DELETE specific definitions ===')

  // זרע — delete def 0 (anatomical: "חומר המיוצר באשכים")
  const zeraAnatomical = getDefId('זרע', 'חומר המיוצר באשכים')
  if (zeraAnatomical) {
    db.prepare('DELETE FROM example WHERE definition_id = ?').run(zeraAnatomical)
    deleteDefinition(zeraAnatomical, 'זרע def 0 (anatomical)')
  }

  // מין — delete def 3 (sexual: "סקס, יחסי אישות")
  const minSexual = getDefId('מין', 'סקס, יחסי אישות')
  if (minSexual) deleteDefinition(minSexual, 'מין def (sexual)')

  // גאה — delete def with "הומוסקסואל"
  const gaahHomo = getDefId('גאה', 'הומוסקסואל')
  if (gaahHomo) deleteDefinition(gaahHomo, 'גאה def (homosexual)')

  // שוביניזם — delete def with "הומוסקסואלים"
  const shobHomo = getDefId('שוביניזם', 'הומוסקסואלים')
  if (shobHomo) deleteDefinition(shobHomo, 'שוביניזם def (homosexual)')

  // נתן — delete def with "הסכים לקיים יחסי מין"
  const natanSex = getDefId('נתן', 'הסכים לקיים יחסי מין')
  if (natanSex) deleteDefinition(natanSex, 'נתן def (sexual slang)')

  // צחק sense 1 — delete def with "יחסי מין"
  const tzachakSex = getDefId('צחק', 'יחסי מין')
  if (tzachakSex) deleteDefinition(tzachakSex, 'צחק def (sexual)')

  // קרב — delete def with "קיים יחסי מין"
  const karavSex = getDefId('קרב (גם: קרב)', 'קיים יחסי מין')
  if (karavSex) deleteDefinition(karavSex, 'קרב def (sexual)')

  // שלישיה — delete def with "יחסי מין בשלישיה"
  const shlishiaSex = getDefId('שלישיה', 'יחסי מין בשלישיה')
  if (shlishiaSex) deleteDefinition(shlishiaSex, 'שלישיה def (sexual)')

  // שמש sense 1 — delete def with "קיים יחסי מין"
  const shamashSex = getDefId('שמש', 'קיים יחסי מין')
  if (shamashSex) deleteDefinition(shamashSex, 'שמש def (sexual)')

  // תשמיש — delete def with "קיום יחסי מין" (keep "הפקת תועלת")
  const tashmishSex = getDefId('תשמיש', 'קיום יחסי מין')
  if (tashmishSex) deleteDefinition(tashmishSex, 'תשמיש def (sexual)')

  // גנח — delete sexual reference from definition
  const ganachDef = db.prepare("SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'גנח'").get()
  if (ganachDef) {
    const cleaned = ganachDef.text.replace(/\s*או יחסי מין[^,.]*/g, '').replace(/\s*\(מעין זעקת "אהה!"\)/g, '').trim()
    updateDefinition(ganachDef.id, cleaned, 'גנח (remove sexual reference)')
  }

  // דפק — delete def with "קיים יחסי מין; זיין"
  const dafakSex = getDefId('דפק', 'קיים יחסי מין')
  if (dafakSex) deleteDefinition(dafakSex, 'דפק def (sexual)')

  // דש — delete def with "הניע גופו בתנועה האופינית בזמן יחסי מין"
  const dashSex = getDefId('דש', 'הניע גופו בתנועה')
  if (dashSex) deleteDefinition(dashSex, 'דש def (sexual)')

  // ידע — delete def with "קיים יחסי מין"
  const yadaSex = getDefId('ידע', 'קיים יחסי מין')
  if (yadaSex) deleteDefinition(yadaSex, 'ידע def (sexual)')

  // הרהר — delete def with "חשב מחשבת זנות"
  const harharZnut = getDefId('הרהר', 'חשב מחשבת זנות')
  if (harharZnut) deleteDefinition(harharZnut, 'הרהר def (זנות)')

  // נבול פה — clean definition
  const navalPehDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'נבול פה'").get()
  if (navalPehDef) updateDefinition(navalPehDef.id, 'דיבור על נושאים שהצנעה יפה להם, שימוש בקללות וכדומה.', 'נבול פה (clean)')

  // בשר — delete def with "איבר מין זכרי"
  const basarSex = getDefId('בשר', 'איבר מין זכרי')
  if (basarSex) deleteDefinition(basarSex, 'בשר def (anatomical)')

  // כיף — delete drug definition, keep Aramaic/pleasure meanings
  const kifDrug = getDefId('כיף', 'מריחואנה')
  if (kifDrug) deleteDefinition(kifDrug, 'כיף def (drug)')
  const kifHashish = getDefId('כיף', 'חשיש')
  if (kifHashish) deleteDefinition(kifHashish, 'כיף def (hashish)')

  // עשב — delete drug definition
  const esevDrug = getDefId('עשב', 'מריחואנה')
  if (esevDrug) deleteDefinition(esevDrug, 'עשב def (drug)')
  const esevDrug2 = getDefId('עשב', 'סם')
  if (esevDrug2) deleteDefinition(esevDrug2, 'עשב def (drug2)')

  console.log('\n=== 4. UPDATE definitions with clean text ===')

  // קרי sense 1 — remove the explicit part, keep "מקרה, אירוע"
  const kariExplicit = getDefId('קרי', 'שכבת זרע הנפלטת')
  if (kariExplicit) deleteDefinition(kariExplicit, 'קרי def (explicit)')

  // מקרה לילה — clean definition
  const makrehLailaDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'מקרה לילה'").get()
  if (makrehLailaDef) updateDefinition(makrehLailaDef.id, 'טומאה הנגרמת בשינה, הנזכרת בתורה (דברים כג, יא).', 'מקרה לילה (clean)')

  // קרי לילה — clean definition
  const kariLailaDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'קרי לילה'").get()
  if (kariLailaDef) updateDefinition(kariLailaDef.id, 'טומאה הנגרמת בשינה, הנזכרת בתורה (דברים כג, יא).', 'קרי לילה (clean)')

  // גלוי עריות — update to clean halachic definition
  const giluyArayotDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'גלוי עריות'").get()
  if (giluyArayotDef) updateDefinition(giluyArayotDef.id, 'איסור תורה — אחד משלושת האיסורים החמורים ביהדות.', 'גלוי עריות (clean)')

  // משכב זכר — update to clean halachic definition
  const mishkavZacharDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'משכב זכר'").get()
  if (mishkavZacharDef) updateDefinition(mishkavZacharDef.id, 'איסור תורה (ויקרא יח, כב).', 'משכב זכר (clean)')

  // בעילה — update to clean halachic definition
  const beilahDef = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'בעילה'").get()
  if (beilahDef) updateDefinition(beilahDef.id, 'אחד מדרכי הקידושין לפי ההלכה.', 'בעילה (clean)')

  // זנה — delete the explicit definition, keep "בגד באמון"
  const zanahExplicit = getDefId('זנה', 'קיים יחסי מין אסורים')
  if (zanahExplicit) {
    // Delete all senses with this definition
    const allZanahDefs = db.prepare("SELECT d.id FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'זנה' AND d.text LIKE '%קיים יחסי מין%'").all()
    allZanahDefs.forEach(d => deleteDefinition(d.id, 'זנה def (explicit)'))
  }
  // Also delete sense 1 of זנה if it only has the explicit definition
  const zanahSenses = db.prepare("SELECT s.id FROM sense s WHERE s.headword = 'זנה'").all()
  zanahSenses.forEach(s => {
    const remaining = db.prepare('SELECT COUNT(*) as c FROM definition WHERE sense_id = ?').get(s.id).c
    if (remaining === 0) {
      db.prepare('DELETE FROM sense WHERE id = ?').run(s.id)
      console.log('  DELETED empty זנה sense')
    }
  })

  // אתנן — update to clean Torah definition
  const atnanDef = getDefId('אתנן', 'עפ"ר זנות')
  if (atnanDef) updateDefinition(atnanDef, 'שכר שניתן לאסורה, האסור להקרבה על גבי המזבח (דברים כג, יט).', 'אתנן (clean)')

  // זנאי — delete modern definition, keep Torah meaning
  const zanaiModern = getDefId('זנאי', 'בלשון מודרנית')
  if (zanaiModern) deleteDefinition(zanaiModern, 'זנאי def (modern)')

  // תשמיש המטה — delete all explicit definitions, keep halachic
  const tashmishHametaDefs = db.prepare("SELECT d.id, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'תשמיש המטה'").all()
  tashmishHametaDefs.forEach(d => {
    if (d.text.includes('יחסי מין') || d.text.includes('יחסי אישות')) {
      deleteDefinition(d.id, 'תשמיש המטה def (explicit)')
    }
  })
  // Add clean halachic definition if none remains
  const tashmishRemaining = db.prepare("SELECT COUNT(*) as c FROM definition d JOIN sense s ON s.id = d.sense_id WHERE s.headword = 'תשמיש המטה'").get().c
  if (tashmishRemaining === 0) {
    const tashmishSense = db.prepare("SELECT id FROM sense WHERE headword = 'תשמיש המטה' LIMIT 1").get()
    if (tashmishSense) {
      const maxOrder = db.prepare('SELECT MAX(def_order) as m FROM definition WHERE sense_id = ?').get(tashmishSense.id).m ?? -1
      db.prepare('INSERT INTO definition (sense_id, text, def_order) VALUES (?, ?, ?)').run(
        tashmishSense.id, 'חיוב הבעל כלפי אשתו לפי ההלכה (שמות כא, י).', maxOrder + 1
      )
      console.log('  ADDED clean definition for תשמיש המטה')
      cleaned++
    }
  }

  // דם בתולים — delete entirely (modern medical term, not needed)
  deleteSense('דם בתולים')

  // Remove inappropriate section_item cross-references
  console.log('\n=== 5. Clean section items ===')
  const inappropriateTerms = ['ארוטי', 'סקסואל', 'הומוסקסו', 'יחסי מין', 'זנות', 'פורנ', 'סטריפ', 'אוננ']
  inappropriateTerms.forEach(term => {
    const items = db.prepare('SELECT id FROM section_item WHERE text LIKE ?').all(`%${term}%`)
    items.forEach(i => {
      db.prepare('DELETE FROM section_item WHERE id = ?').run(i.id)
      cleaned++
    })
    if (items.length > 0) console.log(`  Removed ${items.length} section items containing "${term}"`)
  })

})()

console.log(`\n=== Done: ${deleted} headwords/senses deleted, ${cleaned} definitions cleaned/updated ===`)

// VACUUM
db.exec('VACUUM')
const sizeMB = Math.round(fs.statSync('./public/dicts/wikidictionary.db').size / 1024 / 1024 * 10) / 10
console.log('Final size:', sizeMB, 'MB')
db.close()
