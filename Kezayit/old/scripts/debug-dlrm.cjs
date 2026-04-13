'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// Check what's stored
console.log('── sense rows ──')
db.prepare("SELECT s.headword, s.nikud, s.etymology, src.label FROM sense s LEFT JOIN source src ON src.id=s.source_id WHERE s.headword LIKE '%דלר%'").all()
  .forEach(r => console.log(JSON.stringify(r)))

console.log('\n── definition rows ──')
db.prepare("SELECT d.text, d.sense_id FROM definition d JOIN sense s ON s.id=d.sense_id WHERE s.headword LIKE '%דלר%'").all()
  .forEach(r => console.log(JSON.stringify(r)))

// Check what the source file has
const fs = require('fs')
const iconv = require('iconv-lite')
const raw = fs.readFileSync('C:\\Users\\Admin\\Documents\\ToratEmetInstall\\Dictionaries\\FinalDictionary.txt')
const text = iconv.decode(raw, 'win1255')
const lines = text.split(/\r?\n/).filter(l => l.includes('דלר'))
console.log('\n── source file lines with דלר ──')
lines.forEach(l => console.log(' ', JSON.stringify(l)))

db.close()
