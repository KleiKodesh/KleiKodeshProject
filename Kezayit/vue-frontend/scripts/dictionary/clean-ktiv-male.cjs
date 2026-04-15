'use strict'
/**
 * Nulls out ktiv_male where it's identical to the headword after stripping nikud/diacritics.
 * Also strips wiki markup from ktiv_male values (e.g. [[אבחה -> אבחה).
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

// Strip Hebrew diacritics (nikud + cantillation)
function stripNikud(s) {
  return s ? s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim() : s
}

// Strip wiki markup: [[...]], '''...''', ''...''
function stripWiki(s) {
  if (!s) return s
  return s
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')  // [[link|text]] -> text
    .replace(/\[\[([^\]]+)/g, '$1')                    // unclosed [[
    .replace(/'{2,3}/g, '')
    .trim()
}

const rows = db.prepare('SELECT id, headword, ktiv_male FROM sense WHERE ktiv_male IS NOT NULL').all()
console.log('Total with ktiv_male:', rows.length)

const nullOut = db.prepare('UPDATE sense SET ktiv_male = NULL WHERE id = ?')
const update = db.prepare('UPDATE sense SET ktiv_male = ? WHERE id = ?')

let nulled = 0, cleaned = 0

db.transaction(() => {
  for (const r of rows) {
    const cleaned_ktiv = stripWiki(r.ktiv_male)

    // Null out if identical to headword (ignoring nikud)
    if (stripNikud(cleaned_ktiv) === stripNikud(r.headword) ||
        cleaned_ktiv === r.headword) {
      nullOut.run(r.id)
      nulled++
      continue
    }

    // Update if wiki markup was stripped
    if (cleaned_ktiv !== r.ktiv_male) {
      update.run(cleaned_ktiv, r.id)
      cleaned++
    }
  }
})()

const remaining = db.prepare('SELECT COUNT(*) as c FROM sense WHERE ktiv_male IS NOT NULL').get().c
console.log('Nulled out (identical):', nulled)
console.log('Cleaned (wiki markup):', cleaned)
console.log('Remaining with ktiv_male:', remaining)

// Sample what's left
console.log('\nSample remaining:')
db.prepare('SELECT headword, ktiv_male FROM sense WHERE ktiv_male IS NOT NULL ORDER BY RANDOM() LIMIT 15').all()
  .forEach(r => console.log(r.headword, '->', r.ktiv_male))

db.close()
