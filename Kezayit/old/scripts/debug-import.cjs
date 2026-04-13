'use strict'
const db = require('better-sqlite3')('public/dictionary.db')
db.pragma('foreign_keys = ON')

// Try inserting a test definition
const senseId = db.prepare('SELECT id FROM sense LIMIT 1').get()?.id
console.log('First sense id:', senseId)

if (senseId) {
  try {
    const r = db.prepare('INSERT INTO definition (sense_id, text, def_order) VALUES (?, ?, 0)').run(senseId, 'test')
    console.log('Insert result:', r)
  } catch(e) {
    console.log('Insert error:', e.message)
  }
  const defs = db.prepare('SELECT * FROM definition WHERE sense_id = ?').all(senseId)
  console.log('Defs for sense:', defs)
}

db.close()
