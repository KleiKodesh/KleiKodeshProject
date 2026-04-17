'use strict'
/**
 * scan-wiki-inappropriate.cjs
 * Read-only scan of wikidictionary.db — reports multi-word headwords
 * and definitions/headwords containing inappropriate terms.
 * Run: node scripts/dictionary/scan-wiki-inappropriate.cjs
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'), { readonly: true })

// ── 1. Multi-word headwords ───────────────────────────────────────────────────
const multiWord = db
  .prepare("SELECT headword FROM sense WHERE headword LIKE '% %' GROUP BY headword ORDER BY headword")
  .all()
console.log(`\n=== MULTI-WORD HEADWORDS (${multiWord.length} total) ===`)
multiWord.forEach((r) => console.log(' ', r.headword))

// ── 2. Inappropriate terms in definitions ────────────────────────────────────
const badDefTerms = [
  'ארוטי', 'ארוטיקה', 'סקסואל', 'סקס', 'פורנ', 'אוננ',
  'יחסי מין', 'יחסי אישות', 'איבר מין', 'אשכים', 'שדיים',
  'ביסקסו', 'הומוסקסו', 'לסבי', 'גיי', 'קוויר', 'טרנסג',
  'ליבידו', 'אורגזמ', 'ניאוף', 'זנות', 'עריות',
]
console.log('\n=== INAPPROPRIATE TERMS IN DEFINITIONS ===')
badDefTerms.forEach((term) => {
  const rows = db
    .prepare(
      'SELECT DISTINCT s.headword, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE d.text LIKE ?',
    )
    .all(`%${term}%`)
  if (rows.length) {
    console.log(`[${term}] ${rows.length} hits:`)
    rows.slice(0, 8).forEach((r) => console.log(`  ${r.headword}: ${r.text.slice(0, 100)}`))
  }
})

// ── 3. Inappropriate headwords ───────────────────────────────────────────────
const badHeadTerms = ['סקס', 'פורנ', 'ארוטי', 'הומו', 'לסבי', 'גיי', 'טרנס', 'קוויר', 'ביסקס']
console.log('\n=== INAPPROPRIATE HEADWORDS ===')
badHeadTerms.forEach((term) => {
  const rows = db
    .prepare('SELECT DISTINCT headword FROM sense WHERE headword LIKE ?')
    .all(`%${term}%`)
  if (rows.length) {
    console.log(`[${term}]: ${rows.map((r) => r.headword).join(', ')}`)
  }
})

// ── 4. Inappropriate terms in section_items (cross-refs) ─────────────────────
const badSectionTerms = ['ארוטי', 'סקסואל', 'הומוסקסו', 'יחסי מין', 'זנות', 'פורנ', 'אוננ']
console.log('\n=== INAPPROPRIATE SECTION ITEMS ===')
badSectionTerms.forEach((term) => {
  const rows = db.prepare('SELECT text FROM section_item WHERE text LIKE ?').all(`%${term}%`)
  if (rows.length) {
    console.log(`[${term}] ${rows.length} items:`)
    rows.slice(0, 5).forEach((r) => console.log(`  ${r.text}`))
  }
})

db.close()
