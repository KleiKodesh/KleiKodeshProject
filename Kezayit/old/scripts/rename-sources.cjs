'use strict'
const db = require('better-sqlite3')('public/dictionary.db')
db.pragma('journal_mode = WAL')

const renames = {
  'מילון ארמי':   'תורת אמת - מילון ארמי',
  'מילון ארמי א': 'תורת אמת - מילון ארמי א',
  'מילון ארמי ב': 'תורת אמת - מילון ארמי ב',
  'מילון ארמי ג': 'תורת אמת - מילון ארמי ג',
}

for (const [from, to] of Object.entries(renames)) {
  const r = db.prepare('UPDATE source SET label = ? WHERE label = ?').run(to, from)
  console.log(`${from} → ${to}: ${r.changes} row(s)`)
}

console.log('\nSources now:')
db.prepare('SELECT id, label FROM source ORDER BY id').all()
  .forEach(r => console.log(`  ${r.id}: ${r.label}`))

db.close()
