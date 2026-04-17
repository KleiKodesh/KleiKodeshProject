'use strict'
const Database = require('better-sqlite3')
const path = require('path')

// Check wikidictionary.db
const wiki = new Database(path.resolve('./public/dicts/wikidictionary.db'), { readonly: true })
const wikiTables = wiki.prepare("SELECT name FROM sqlite_master WHERE type='table'").all()
console.log('wikidictionary tables:', wikiTables.map(t => t.name))

// Search in all text columns for אהבה אפלטונית
for (const t of wikiTables) {
  const cols = wiki.prepare(`PRAGMA table_info(${t.name})`).all()
  const textCols = cols.filter(c => c.type.toUpperCase().includes('TEXT')).map(c => c.name)
  for (const col of textCols) {
    try {
      const rows = wiki.prepare(`SELECT * FROM ${t.name} WHERE ${col} LIKE '%אהבה אפלטונית%' LIMIT 3`).all()
      if (rows.length) console.log(`FOUND in wiki ${t.name}.${col}:`, rows)
      const rows2 = wiki.prepare(`SELECT * FROM ${t.name} WHERE ${col} LIKE '%אפלטונית%' LIMIT 3`).all()
      if (rows2.length) console.log(`FOUND אפלטונית in wiki ${t.name}.${col}:`, rows2)
    } catch {}
  }
}
wiki.close()

// Check kezayit_dictionary.db
const kez = new Database(path.resolve('./public/dicts/kezayit_dictionary.db'), { readonly: true })
const kezTables = kez.prepare("SELECT name FROM sqlite_master WHERE type='table'").all()
console.log('\nkezayit_dictionary tables:', kezTables.map(t => t.name))

for (const t of kezTables) {
  const cols = kez.prepare(`PRAGMA table_info(${t.name})`).all()
  const textCols = cols.filter(c => c.type.toUpperCase().includes('TEXT')).map(c => c.name)
  for (const col of textCols) {
    try {
      const rows = kez.prepare(`SELECT * FROM ${t.name} WHERE ${col} LIKE '%אפלטונית%' LIMIT 3`).all()
      if (rows.length) console.log(`FOUND in kez ${t.name}.${col}:`, rows)
      const rows2 = kez.prepare(`SELECT * FROM ${t.name} WHERE ${col} LIKE '%ארוטי%' LIMIT 3`).all()
      if (rows2.length) console.log(`FOUND ארוטי in kez ${t.name}.${col}:`, rows2)
    } catch {}
  }
}
kez.close()
