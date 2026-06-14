/**
 * Worker thread for dev-mode SQLite queries.
 * Each worker holds its own SQLite connection — queries on different workers
 * run truly in parallel, so concurrent frontend requests don't serialize.
 */
'use strict'
const { workerData, parentPort } = require('node:worker_threads')
const Database = require('better-sqlite3')
const path = require('node:path')
const fs = require('node:fs')

const { dbPath, dictDbPath, userSettingsDbPath } = workerData

let db = null
let dictDb = null
let userSettingsDb = null

try {
  db = new Database(path.resolve(dbPath))
} catch (err) {
  console.error('[dev-sqlite-worker] failed to open main DB:', err.message)
}

try {
  dictDb = new Database(dictDbPath, { readonly: true })
} catch (err) {
  console.error('[dev-sqlite-worker] failed to open dictionary DB:', err.message)
}

try {
  if (fs.existsSync(userSettingsDbPath)) {
    userSettingsDb = new Database(userSettingsDbPath)
    userSettingsDb.exec(`
      CREATE TABLE IF NOT EXISTS user_highlights (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
        startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
        colorArgb INTEGER NOT NULL, createdAt INTEGER NOT NULL
      );
      CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line ON user_highlights (bookId, lineId);
      CREATE TABLE IF NOT EXISTS user_notes (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
        startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
        note TEXT NOT NULL, quote TEXT NOT NULL,
        createdAt INTEGER NOT NULL, updatedAt INTEGER NOT NULL
      );
      CREATE INDEX IF NOT EXISTS idx_user_notes_book_line ON user_notes (bookId, lineId);
    `)
  }
} catch (err) {
  console.warn('[dev-sqlite-worker] could not open user_settings.db:', err.message)
}

parentPort.on('message', ({ requestId, type, sql, params }) => {
  try {
    if (type === 'exec-user-settings') {
      if (!userSettingsDb) {
        parentPort.postMessage({ requestId, error: 'User settings DB not available' })
        return
      }
      const result = userSettingsDb.prepare(sql).run(...params)
      parentPort.postMessage({ requestId, lastInsertId: result.lastInsertRowid })
      return
    }

    const target =
      type === 'query-dict'          ? dictDb :
      type === 'query-user-settings' ? userSettingsDb :
      db

    if (!target) {
      parentPort.postMessage({ requestId, error: 'Database not available' })
      return
    }

    const rows = target.prepare(sql).all(...params)
    parentPort.postMessage({ requestId, rows })
  } catch (err) {
    parentPort.postMessage({ requestId, error: err.message })
  }
})
