'use strict'
/**
 * deep-scan-inappropriate.cjs
 * Comprehensive scan of wikidictionary.db for mature/inappropriate content.
 * Checks headwords, definitions, and section_items.
 * Read-only — no changes made.
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'), { readonly: true })

function scanDefs(label, terms) {
  let found = false
  for (const term of terms) {
    const rows = db
      .prepare(
        'SELECT DISTINCT s.headword, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE d.text LIKE ?',
      )
      .all(`%${term}%`)
    if (rows.length) {
      found = true
      console.log(`  [${term}] ${rows.length} hits:`)
      rows.forEach((r) => console.log(`    "${r.headword}" → ${r.text.slice(0, 100)}`))
    }
  }
  if (!found) console.log('  (none)')
}

function scanHeadwords(label, terms) {
  let found = false
  for (const term of terms) {
    const rows = db
      .prepare('SELECT DISTINCT headword FROM sense WHERE headword LIKE ?')
      .all(`%${term}%`)
    if (rows.length) {
      found = true
      console.log(`  [${term}]: ${rows.map((r) => r.headword).join(' | ')}`)
    }
  }
  if (!found) console.log('  (none)')
}

function scanSectionItems(terms) {
  let found = false
  for (const term of terms) {
    const rows = db.prepare('SELECT DISTINCT text FROM section_item WHERE text LIKE ?').all(`%${term}%`)
    if (rows.length) {
      found = true
      console.log(`  [${term}] ${rows.length} items: ${rows.slice(0, 5).map((r) => r.text).join(' | ')}`)
    }
  }
  if (!found) console.log('  (none)')
}

// ── Sexual / erotic terms (Hebrew root variations) ────────────────────────────
console.log('\n=== SEXUAL / EROTIC TERMS IN DEFINITIONS ===')
scanDefs('sexual', [
  // erotic root ארט
  'ארוטי', 'ארוטית', 'ארוטיים', 'ארוטיות', 'ארוטיקה', 'ארוטיזם', 'ארוטי',
  // sex
  'סקסי', 'סקסית', 'סקסואל', 'סקסיות',
  // sexual act roots
  'יחסי מין', 'יחסי אישות', 'קיים יחסי', 'מעשה מין', 'מגע מיני', 'מיני',
  'בעל אשה', 'בא עליה', 'שכב עמה', 'שכב עם',
  // body parts
  'איבר מין', 'איבר המין', 'פין ', 'ערווה', 'ערוה', 'בתולים', 'בתולין',
  'אשכים', 'שדיים', 'שד ', 'חזה האישה', 'אורגן מיני',
  // climax/arousal
  'אורגזמ', 'ליבידו', 'עוררות', 'גירוי מיני', 'תשוקה מינית',
  // masturbation
  'אוננ', 'מאונן',
  // intercourse euphemisms
  'מגע אינטימי', 'יחסי קרבה', 'חיי אישות',
  // pornography
  'פורנוגרפ', 'פורנ',
  // fetish/BDSM
  'פטיש מיני', 'בדסמ', 'בד"סמ',
  // prostitution
  'זונה', 'זנות', 'זנה', 'ניאוף', 'אתנן',
  // LGBT explicit
  'הומוסקסו', 'לסביות', 'לסבית', 'ביסקסו', 'קוויר', 'דו-מיני', 'דומיני',
  'יחסי מין חד',
  // rape
  'אונס', 'אנס', 'אנוס מינית',
  // genitalia slang
  'פות', 'כוס ', 'זין ',
  // sperm/ejaculation
  'זרע ', 'שפיכה', 'קרי',
])

// ── Mature headwords ──────────────────────────────────────────────────────────
console.log('\n=== MATURE/INAPPROPRIATE HEADWORDS ===')
scanHeadwords('headwords', [
  'ארוטי', 'סקסי', 'סקסואל', 'פורנ', 'זונה', 'זנות',
  'אוננ', 'הומוסקסו', 'לסבי', 'ביסקס', 'קוויר',
  'אורגזמ', 'ליבידו', 'בדסמ', 'אנס ', 'אינוס',
])

// ── Section items ─────────────────────────────────────────────────────────────
console.log('\n=== INAPPROPRIATE SECTION ITEMS (remaining) ===')
scanSectionItems([
  'ארוטי', 'סקסי', 'סקסואל', 'פורנ', 'זונה', 'זנות',
  'אוננ', 'הומוסקסו', 'לסבי', 'ביסקס', 'קוויר',
  'אורגזמ', 'ליבידו', 'יחסי מין', 'אונס', 'אינוס',
  'איבר מין', 'אשכים', 'שדיים',
])

// ── Examples table ────────────────────────────────────────────────────────────
console.log('\n=== INAPPROPRIATE TERMS IN EXAMPLES ===')
const exampleBadTerms = [
  'ארוטי', 'ארוטית', 'יחסי מין', 'יחסי אישות', 'מיני', 'מינית',
  'אשכים', 'שדיים', 'איבר מין', 'פורנ', 'זנות', 'אונס',
]
let foundInExamples = false
for (const term of exampleBadTerms) {
  const rows = db
    .prepare(
      'SELECT DISTINCT s.headword, e.text FROM example e JOIN definition d ON d.id = e.definition_id JOIN sense s ON s.id = d.sense_id WHERE e.text LIKE ?',
    )
    .all(`%${term}%`)
  if (rows.length) {
    foundInExamples = true
    console.log(`  [${term}] ${rows.length} hits:`)
    rows.slice(0, 5).forEach((r) => console.log(`    "${r.headword}" example: ${r.text.slice(0, 100)}`))
  }
}
if (!foundInExamples) console.log('  (none)')

db.close()
