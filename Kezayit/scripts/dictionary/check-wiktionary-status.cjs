const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })
const count = db.prepare('SELECT COUNT(*) as c FROM entry WHERE source=25').get().c
const cont = db.prepare("SELECT value FROM meta WHERE key='wiktionary_apcontinue'").get()?.value
const done = db.prepare("SELECT value FROM meta WHERE key='wiktionary_done'").get()?.value
console.log('Wiktionary entries:', count)
console.log('Continue point:', cont || '(beginning)')
console.log('Done flag:', done || 'not set')
// Sample entries
const sample = db.prepare('SELECT headword, nikud, definition FROM entry WHERE source=25 LIMIT 10').all()
console.log('\nSample entries:')
sample.forEach(r => console.log(`  ${r.nikud || r.headword} → ${r.definition.substring(0, 80)}`))
db.close()
