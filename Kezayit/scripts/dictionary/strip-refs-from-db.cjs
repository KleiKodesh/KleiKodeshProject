'use strict'
/**
 * Strips <ref>...</ref> tags and their content from all text fields in wikidictionary.db.
 * Also strips bare URLs and wiki markup remnants.
 * Runs in-place on the DB.
 */
const Database = require('better-sqlite3')
const path = require('path')

const db = new Database(path.resolve('./public/dicts/wikidictionary.db'))
db.pragma('journal_mode = DELETE')

function clean(text) {
  if (!text) return text
  return text
    .replace(/<ref[^>]*>[\s\S]*?<\/ref>/gi, '')  // <ref>content</ref>
    .replace(/<ref[^>]*\/>/gi, '')                 // <ref/>
    .replace(/<ref[^>]*>/gi, '')                   // unclosed <ref>
    .replace(/<\/ref>/gi, '')                      // stray </ref>
    .replace(/<[^>]+>/g, '')                       // any other HTML tags
    .replace(/\[https?:\/\/[^\]]*\]/g, '')         // [http://... text]
    .replace(/https?:\/\/\S+/g, '')                // bare URLs
    .replace(/'{2,3}/g, '')                        // wiki bold/italic
    .replace(/\s+/g, ' ')
    .trim()
}

// Clean sense.headword and sense.nikud
const senses = db.prepare('SELECT id, headword, nikud, ktiv_male FROM sense').all()
let senseCleaned = 0
const updateSense = db.prepare('UPDATE sense SET headword = ?, nikud = ?, ktiv_male = ? WHERE id = ?')
db.transaction(() => {
  for (const r of senses) {
    const ch = clean(r.headword)
    const cn = r.nikud ? clean(r.nikud) : r.nikud
    const ck = r.ktiv_male ? clean(r.ktiv_male) : r.ktiv_male
    if (ch !== r.headword || cn !== r.nikud || ck !== r.ktiv_male) {
      updateSense.run(ch, cn, ck, r.id)
      senseCleaned++
    }
  }
})()
console.log('headwords/nikud/ktiv_male cleaned:', senseCleaned, '/', senses.length)

// Clean definition.text
const defs = db.prepare('SELECT id, text FROM definition').all()
let defCleaned = 0
const updateDef = db.prepare('UPDATE definition SET text = ? WHERE id = ?')
db.transaction(() => {
  for (const r of defs) {
    const c = clean(r.text)
    if (c !== r.text) { updateDef.run(c, r.id); defCleaned++ }
  }
})()
console.log('definitions cleaned:', defCleaned, '/', defs.length)

// Clean section_item.text
const items = db.prepare('SELECT id, text FROM section_item').all()
let itemCleaned = 0
const updateItem = db.prepare('UPDATE section_item SET text = ? WHERE id = ?')
db.transaction(() => {
  for (const r of items) {
    const c = clean(r.text)
    if (c !== r.text) { updateItem.run(c, r.id); itemCleaned++ }
  }
})()
console.log('section_items cleaned:', itemCleaned, '/', items.length)

// Clean example.source — strip URLs, keep the citation text after them
const examples = db.prepare('SELECT id, text, source FROM example').all()
let exCleaned = 0
const updateEx = db.prepare('UPDATE example SET text = ?, source = ? WHERE id = ?')
db.transaction(() => {
  for (const r of examples) {
    const ct = clean(r.text)
    // For source: strip URL prefix, keep the rest (author/title)
    const cs = r.source ? r.source.replace(/https?:\/\/\S+\s*/g, '').trim() || null : r.source
    if (ct !== r.text || cs !== r.source) { updateEx.run(ct, cs, r.id); exCleaned++ }
  }
})()
console.log('examples cleaned:', exCleaned, '/', examples.length)

db.close()
console.log('Done.')
