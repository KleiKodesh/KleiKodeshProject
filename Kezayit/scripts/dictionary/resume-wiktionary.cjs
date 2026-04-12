const db = require('better-sqlite3')('public/dictionary.db')
db.prepare("DELETE FROM meta WHERE key='wiktionary_done'").run()
console.log('Cleared done flag — will resume from:', 
  db.prepare("SELECT value FROM meta WHERE key='wiktionary_apcontinue'").get()?.value)
db.close()
