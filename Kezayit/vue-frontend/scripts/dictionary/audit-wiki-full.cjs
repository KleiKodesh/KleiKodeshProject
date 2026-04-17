'use strict'
/**
 * audit-wiki-full.cjs
 * Comprehensive audit for remaining problematic content.
 * Checks headwords directly, definitions, and examples across many categories:
 * - Explicit sexual content (remaining)
 * - Drug-related
 * - Violence/gore
 * - Anti-religious / blasphemous
 * - Racist / slurs
 * - LGBTQ explicit (headwords as topics)
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'), { readonly: true })

function scanDefs(label, terms) {
  console.log(`\n--- ${label} ---`)
  let found = false
  for (const term of terms) {
    const rows = db.prepare(
      'SELECT DISTINCT s.headword, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE d.text LIKE ?'
    ).all(`%${term}%`)
    // Filter out obvious false positives for short terms
    const realHits = rows.filter(r => {
      const t = r.text
      // Skip if "term" appears only as part of a clearly unrelated word
      return true // we'll review manually
    })
    if (realHits.length) {
      found = true
      console.log(`  [${term}] ${realHits.length} hits:`)
      realHits.slice(0, 6).forEach(r =>
        console.log(`    "${r.headword}": ${r.text.slice(0, 110)}`)
      )
    }
  }
  if (!found) console.log('  (none)')
}

function scanHeadwords(label, terms) {
  console.log(`\n--- Headwords: ${label} ---`)
  let found = false
  for (const term of terms) {
    const rows = db.prepare(
      'SELECT DISTINCT headword FROM sense WHERE headword LIKE ? OR headword = ?'
    ).all(`%${term}%`, term)
    if (rows.length) {
      found = true
      console.log(`  [${term}]: ${rows.map(r => r.headword).join(' | ')}`)
    }
  }
  if (!found) console.log('  (none)')
}

function scanExamples(label, terms) {
  console.log(`\n--- Examples: ${label} ---`)
  let found = false
  for (const term of terms) {
    const rows = db.prepare(
      'SELECT DISTINCT s.headword, e.text FROM example e JOIN definition d ON d.id = e.definition_id JOIN sense s ON s.id = d.sense_id WHERE e.text LIKE ?'
    ).all(`%${term}%`)
    if (rows.length) {
      found = true
      console.log(`  [${term}] ${rows.length} hits:`)
      rows.slice(0, 4).forEach(r =>
        console.log(`    "${r.headword}": ${r.text.slice(0, 110)}`)
      )
    }
  }
  if (!found) console.log('  (none)')
}

// ── Sexual / body ─────────────────────────────────────────────────────────────
scanDefs('Sexual / body terms in definitions', [
  'מין מיני', 'תשוקה מינית', 'עוררות מינית', 'גירוי מיני', 'מגע מיני',
  'יצר המין', 'יצר הרע', 'חיי מין', 'חיי אישות',
  'בעל אותה', 'שכב עמה', 'בא עליה', 'ידע אותה',
  'ביאה', 'גילוי עריות', 'עריות', 'ערוה',
  'בתולה', 'בתולים', 'בתולין', 'פריה ורביה',
  'פות', 'אבר מין', 'זרע האדם', 'ביצים',
  'חזה', 'עירום', 'ערום', 'עירומה',
  'מחשוף', 'בגד ים', 'ביקיני',
  'הריון', 'לידה', 'הפלה', 'מניעת הריון',
  'גנוסטי', 'אפרודיטה', 'אפרודיזיאק',
])

scanDefs('LGBT / gender ideology terms', [
  'להט"ב', 'להטב', 'גאווה', 'טרנסג\'נדר', 'טרנסג', 'טרנסקסו',
  'חד מין', 'בן מינו', 'בת מינה', 'זוגיות חד',
  'נישואים חד', 'אימוץ חד', 'הורות חד',
  'זהות מגדרית', 'מגדר', 'שינוי מין', 'הסבת מין',
  'גבר לאישה', 'אישה לגבר',
  'שוויון זוגי', 'אוריינטציה מינית',
])

scanDefs('Drugs', [
  'מריחואנה', 'קנאביס', 'סם מסם', 'סמים', 'סם קשה',
  'הרואין', 'קוקאין', 'אקסטזי', 'LSD', 'כדורים',
  'הזיה', 'הזיות', 'נרקוטי', 'אופיום', 'מורפין',
  'חשיש', 'כיף חשיש',
])

scanDefs('Violence / gore', [
  'רצח', 'הרג', 'שחיטה', 'עינוי', 'עינויים',
  'אכזריות', 'ניתוח גופה', 'נתיחה', 'פגר',
])

scanDefs('Anti-religion / blasphemy', [
  'כפירה', 'אתאיזם', 'אתאיסט', 'כופר',
  'עבודה זרה', 'אלילים', 'אליל', 'פסל',
  'ביקורת התורה', 'ביקורת המקרא', 'מינות',
])

scanDefs('Racism / slurs', [
  'נגרו', 'כושי', 'שחורי', 'ערבי טרור',
  'גזענות', 'אפרטהייד', 'שואה', 'נאציזם',
])

// ── Headwords that are topics to filter ───────────────────────────────────────
scanHeadwords('Explicit headwords', [
  'זונה', 'אונס', 'סקס', 'ביאה',
  'עריות', 'ערוה', 'פות', 'ביקיני',
  'לסבית', 'הומוסקסואל', 'ביסקסואל', 'טרנסג\'נדר',
  'אקסטזי', 'הרואין', 'קוקאין', 'מריחואנה', 'קנאביס',
  'כושי', 'נגרו',
])

// ── Examples ──────────────────────────────────────────────────────────────────
scanExamples('Inappropriate examples', [
  'ארוטי', 'ארוטית', 'יחסי מין', 'יחסי אישות',
  'מגע מיני', 'עוררות מינית', 'תשוקה מינית',
  'מריחואנה', 'קוקאין', 'הרואין',
])

db.close()
