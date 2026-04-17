'use strict'
const db = require('better-sqlite3')('./public/dicts/wikidictionary.db', { readonly: true })
const r = db.prepare("SELECT COUNT(*) as c FROM sense WHERE headword LIKE '% %'").get()
console.log('multi-word senses:', r.c)
const r2 = db.prepare("SELECT s.headword, d.text FROM definition d JOIN sense s ON s.id = d.sense_id WHERE d.text LIKE '%ארוטי%' LIMIT 3").all()
console.log('ארוטי hits:', r2)
const r3 = db.prepare("SELECT headword FROM sense WHERE headword = 'אהבה אפלטונית' LIMIT 1").get()
console.log('אהבה אפלטונית exists:', r3)
db.close()
