'use strict'
const Database = require('better-sqlite3')
const db = new Database('./public/dicts/wikidictionary.db', { readonly: true })
const words = ['זין','שיט','בורדל','מציצה','מיניות','שלום','ספר','תורה','אחלה','ממזר','דפוק','זבול','חר','דבע','ערס','שוונץ','נושך כריות']
const rows = db.prepare(`SELECT s.headword, GROUP_CONCAT(d.text, ' | ') as defs FROM sense s JOIN definition d ON d.sense_id = s.id WHERE s.headword IN (${words.map(()=>'?').join(',')}) GROUP BY s.headword`).all(...words)
rows.forEach(r => console.log(r.headword, '->', r.defs.substring(0, 200)))
db.close()
