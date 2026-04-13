'use strict'
const db = require('better-sqlite3')('public/dictionary.db', { readonly: true })

// Check the index on headword
console.log('── Indexes on sense ──')
db.prepare("SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='sense'").all()
  .forEach(r => console.log(`  ${r.name}: ${r.sql}`))

// EXPLAIN prefix search
console.log('\n── Query plan: prefix (word%) ──')
db.prepare('EXPLAIN QUERY PLAN SELECT s.headword, src.label, GROUP_CONCAT(d.text, \', \') FROM sense s LEFT JOIN source src ON src.id=s.source_id JOIN definition d ON d.sense_id=s.id WHERE s.headword LIKE ? GROUP BY s.headword, s.source_id LIMIT 50').all('דיל%')
  .forEach(r => console.log(' ', r.detail))

// EXPLAIN contains search
console.log('\n── Query plan: contains (%word%) ──')
db.prepare('EXPLAIN QUERY PLAN SELECT s.headword, src.label, GROUP_CONCAT(d.text, \', \') FROM sense s LEFT JOIN source src ON src.id=s.source_id JOIN definition d ON d.sense_id=s.id WHERE s.headword LIKE ? GROUP BY s.headword, s.source_id LIMIT 50').all('%דיל%')
  .forEach(r => console.log(' ', r.detail))

// Benchmark both
const N = 200
console.log(`\n── Timing (${N} runs each) ──`)

const prefixStmt = db.prepare('SELECT headword FROM sense WHERE headword LIKE ? LIMIT 50')
let t = Date.now()
for (let i = 0; i < N; i++) prefixStmt.all('דיל%')
console.log(`  prefix (word%):   ${Date.now() - t}ms`)

const containsStmt = db.prepare('SELECT headword FROM sense WHERE headword LIKE ? LIMIT 50')
t = Date.now()
for (let i = 0; i < N; i++) containsStmt.all('%דיל%')
console.log(`  contains (%word%): ${Date.now() - t}ms`)

db.close()
