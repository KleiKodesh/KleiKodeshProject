'use strict'
const db = require('better-sqlite3')('dist/dictionary.db', { readonly: true })

// After *** splitting, what patterns remain in definition text?
const entries = db.prepare("SELECT headword, nikud, definition FROM entry WHERE type='aramaic'").all()

// Collect all individual segments (after *** split)
const segments = []
for (const e of entries) {
  for (const seg of e.definition.split('***')) {
    const s = seg.trim()
    if (!s) continue
    // Strip {nikud} prefix if present
    const withoutNikud = s.replace(/^\{[^}]+\}\s*/, '').trim()
    segments.push({ headword: e.headword, raw: s, text: withoutNikud })
  }
}

console.log(`Total segments: ${segments.length}`)

// (=...) expansion pattern
const withExpansion = segments.filter(s => s.text.match(/^\(=[^)]+\)/))
console.log(`\n── Segments starting with (=expansion): ${withExpansion.length} ──`)
withExpansion.slice(0, 15).forEach(s => console.log(`  [${s.headword}] ${s.text}`))

// Segments where (=...) is in the middle
const expansionMid = segments.filter(s => !s.text.match(/^\(=/) && s.text.includes('(='))
console.log(`\n── Segments with (=expansion) in middle: ${expansionMid.length} ──`)
expansionMid.slice(0, 10).forEach(s => console.log(`  [${s.headword}] ${s.text}`))

// Segments with trailing parenthetical (not =)
const withParen = segments.filter(s => s.text.match(/\([^=)][^)]*\)/))
console.log(`\n── Segments with (note) parenthetical: ${withParen.length} ──`)
withParen.slice(0, 10).forEach(s => console.log(`  [${s.headword}] ${s.text}`))

// Check for any remaining {nikud} that weren't at the start
const remainingNikud = segments.filter(s => s.text.includes('{'))
console.log(`\n── Segments with remaining { after nikud strip: ${remainingNikud.length} ──`)
remainingNikud.slice(0, 5).forEach(s => console.log(`  [${s.headword}] ${s.raw}`))

// What does a clean definition look like after all parsing?
console.log('\n── Sample clean definitions ──')
segments.slice(0, 10).forEach(s => console.log(`  [${s.headword}] "${s.text}"`))

db.close()
