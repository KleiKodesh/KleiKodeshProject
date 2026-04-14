'use strict'
/**
 * Removes examples whose source is not a Torah/classical source.
 * Also removes edge cases: kept sources that have an author name after a comma
 * (Torah sources are "ספר פרק, פסוק" — never "title, author").
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

const TORAH_BOOKS = new Set([
  'בראשית','שמות','ויקרא','במדבר','דברים',
  'יהושע','שופטים','שמואל','מלכים','ישעיהו','ירמיהו','יחזקאל',
  'הושע','יואל','עמוס','עובדיה','יונה','מיכה','נחום','חבקוק','צפניה','חגי','זכריה','מלאכי',
  'תהלים','משלי','איוב','שיר השירים','רות','איכה','קהלת','אסתר','דניאל','עזרא','נחמיה','דברי הימים',
  'ברכות','פאה','דמאי','כלאים','שביעית','תרומות','מעשרות','מעשר שני','חלה','ערלה','ביכורים',
  'שבת','עירובין','פסחים','שקלים','יומא','סוכה','ביצה','ראש השנה','תענית','מגילה','מועד קטן','חגיגה',
  'יבמות','כתובות','נדרים','נזיר','סוטה','גיטין','קידושין',
  'בבא קמא','בבא מציעא','בבא בתרא','סנהדרין','מכות','שבועות','עדויות','עבודה זרה','אבות','הוריות',
  'זבחים','מנחות','חולין','בכורות','ערכין','תמורה','כריתות','מעילה','תמיד','מידות','קינים',
  'כלים','אהלות','נגעים','פרה','טהרות','מקוואות','נידה','מכשירין','זבים','טבול יום','ידים','עוקצין',
  'אבות דרבי נתן','מדרש','ספרי','ספרא','מכילתא','תוספתא',
  'בראשית רבה','שמות רבה','ויקרא רבה','במדבר רבה','דברים רבה',
  'מדרש רבה','מדרש תנחומא','פסיקתא','ילקוט שמעוני',
  'רמב"ם','משנה תורה','שולחן ערוך','טור','ספר המצוות',
  'זוהר','תיקוני זוהר',
])

// Known Torah authors (medieval/classical only)
const TORAH_AUTHORS = new Set([
  'רבי יהודה הלוי', 'ר\' יהודה הלוי', 'יהודה הלוי',
  'רמב"ם', 'רש"י', 'רמב"ן', 'ראב"ע', 'רשב"ם',
  'ר\' שמואל הנגיד', 'שמואל הנגיד',
])

function stripWiki(s) {
  return s
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')
    .replace(/'{2,3}/g, '')
    .replace(/\{\{[^}]*\}\}/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

function isTorahSource(raw) {
  if (!raw) return false
  const s = stripWiki(raw.trim())
  if (!s) return false
  // Has Latin chars → not Torah
  if (/[a-zA-Z]/.test(s)) return false
  // Starts with numbers → Ben Yehuda ID
  if (/^\d/.test(s)) return false
  // Starts with quote marks → modern book title
  if (s.startsWith('"') || s.startsWith('"')) return false

  // Check for Torah book name
  let hasTorahBook = false
  for (const book of TORAH_BOOKS) {
    if (s.includes(book)) { hasTorahBook = true; break }
  }

  if (!hasTorahBook) {
    // Try pure chapter/verse pattern
    if (!/^[\u05D0-\u05EA"' ]+[,\s]+[\u05D0-\u05EA\d, ]+$/.test(s)) return false
  }

  // Edge case: has a comma followed by a person's name (not a chapter/verse)
  // Torah sources: "ספר פרק, פסוק" — the part after comma is Hebrew letters/numbers only
  // Modern sources: "title, author name" — author name has spaces and multiple words
  const commaIdx = s.lastIndexOf(',')
  if (commaIdx > 0) {
    const afterComma = s.slice(commaIdx + 1).trim()
    // If after the comma there are multiple Hebrew words (author name), it's modern
    // Torah verse refs after comma are single Hebrew letter(s) or number(s): "א", "יד", "נב"
    const words = afterComma.split(/\s+/).filter(Boolean)
    if (words.length > 2) {
      // More than 2 words after comma = likely an author name, not a verse
      // But check if it's a known Torah author
      const isKnownAuthor = [...TORAH_AUTHORS].some(a => s.includes(a))
      if (!isKnownAuthor) return false
    }
  }

  return true
}

const allEx = db.prepare('SELECT id, source FROM example WHERE source IS NOT NULL').all()
const toDelete = allEx.filter(r => !isTorahSource(r.source))
const toKeep = allEx.filter(r => isTorahSource(r.source))

console.log('Examples to DELETE:', toDelete.length)
console.log('Examples to KEEP:', toKeep.length)

// Sample what we're deleting
console.log('\nSample deleted:')
toDelete.slice(0, 10).forEach(r => console.log(' ', r.source?.substring(0, 80)))

// Sample what we're keeping
console.log('\nSample kept:')
toKeep.slice(0, 10).forEach(r => console.log(' ', r.source?.substring(0, 80)))

// Execute deletion
const del = db.prepare('DELETE FROM example WHERE id = ?')
db.transaction(() => toDelete.forEach(r => del.run(r.id)))()
console.log('\nDeleted', toDelete.length, 'non-Torah examples.')

// Also delete examples with no source and no text (cleanup)
const emptyDel = db.prepare("DELETE FROM example WHERE (source IS NULL OR source = '') AND (text IS NULL OR text = '')").run()
console.log('Deleted', emptyDel.changes, 'empty examples.')

db.pragma('optimize')
db.close()
