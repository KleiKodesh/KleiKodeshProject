'use strict'
const Database = require('better-sqlite3')
const db = new Database('./public/dicts/wikidictionary.db')
db.pragma('journal_mode = DELETE')

const TORAH_BOOKS = new Set([
  'בראשית','שמות','ויקרא','במדבר','דברים','יהושע','שופטים','שמואל','מלכים',
  'ישעיהו','ירמיהו','יחזקאל','הושע','יואל','עמוס','עובדיה','יונה','מיכה',
  'נחום','חבקוק','צפניה','חגי','זכריה','מלאכי','תהלים','משלי','איוב',
  'שיר השירים','רות','איכה','קהלת','אסתר','דניאל','עזרא','נחמיה','דברי הימים',
  'ברכות','שבת','עירובין','פסחים','שקלים','יומא','סוכה','ביצה','ראש השנה',
  'תענית','מגילה','מועד קטן','חגיגה','יבמות','כתובות','נדרים','נזיר','סוטה',
  'גיטין','קידושין','בבא קמא','בבא מציעא','בבא בתרא','סנהדרין','מכות',
  'שבועות','עדויות','עבודה זרה','אבות','הוריות','זבחים','מנחות','חולין',
  'בכורות','ערכין','תמורה','כריתות','מעילה','תמיד','מידות','קינים',
  'כלים','אהלות','נגעים','פרה','טהרות','מקוואות','נידה','מכשירין','זבים',
  'טבול יום','ידים','עוקצין','פאה','דמאי','כלאים','שביעית','תרומות',
  'מעשרות','מעשר שני','חלה','ערלה','ביכורים','מדות',
  'אבות דרבי נתן','מדרש','ספרי','ספרא','מכילתא','תוספתא',
  'בראשית רבה','שמות רבה','ויקרא רבה','במדבר רבה','דברים רבה',
  'מדרש רבה','מדרש תנחומא','פסיקתא','ילקוט שמעוני',
  'רמב"ם','משנה תורה','שולחן ערוך','זוהר',
])

const CLASSICAL_AUTHORS = ['רבי יהודה הלוי', 'ר\' יהודה הלוי', 'שמואל הנגיד', 'ר\' שמואל הנגיד',
  'הרודוטוס', 'איסופוס', 'הוראטיוס', 'לוקיאנוס', 'פילון', 'יוסף פלביוס',
  'יהודה אלחריזי', 'ר\' יהודה אלחריזי']

function isModernSource(s) {
  if (!s) return false
  if (/[a-zA-Z]/.test(s)) return false
  for (const b of TORAH_BOOKS) if (s.includes(b)) return false
  for (const a of CLASSICAL_AUTHORS) if (s.includes(a)) return false
  const commaIdx = s.lastIndexOf(',')
  if (commaIdx > 0) {
    const after = s.slice(commaIdx + 1).trim()
    const words = after.split(/\s+/).filter(Boolean)
    if (words.length > 1) return true
    if (words.length === 1 && after.length > 8) return true
  }
  return false
}

const all = db.prepare('SELECT id, source FROM example WHERE source IS NOT NULL').all()
const toDelete = all.filter(r => isModernSource(r.source))
console.log('Deleting:', toDelete.length, 'modern examples')

const del = db.prepare('DELETE FROM example WHERE id = ?')
db.transaction(() => toDelete.forEach(r => del.run(r.id)))()

db.exec('VACUUM')
const fs = require('fs')
console.log('Done. Size:', Math.round(fs.statSync('./public/dicts/wikidictionary.db').size/1024/1024*10)/10, 'MB')
console.log('Remaining examples:', db.prepare('SELECT COUNT(*) as c FROM example').get().c)
db.close()
