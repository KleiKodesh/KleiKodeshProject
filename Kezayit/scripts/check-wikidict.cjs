'use strict'
const db = require('better-sqlite3')('./public/wikidictionary.db')
db.pragma('wal_checkpoint(TRUNCATE)')
const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(r => r.name)
console.log('tables:', tables.join(', '))
console.log('senses:', db.prepare('SELECT COUNT(*) as c FROM sense').get().c)
console.log('meta:', JSON.stringify(db.prepare('SELECT * FROM _meta').all()))
db.close()
