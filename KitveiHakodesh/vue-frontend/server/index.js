/**
 * Dev SQLite server.
 * Run: node server/index.js
 *
 * Expects DB_PATH env var (default: ./data.db)
 * Listens on PORT env var (default: 4000)
 *
 * POST /query  { sql, params? }  →  { rows }
 */

import Database from 'better-sqlite3'
import http from 'node:http'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

const DB_PATH = process.env.DB_PATH ?? './data.db'
const DICT_DB_PATH = process.env.DICT_DB_PATH ?? path.join(__dirname, '../public/dictionary/KitveiHakodesh_dictionary.db')
const PORT = process.env.PORT ?? 4000

const db = new Database(path.resolve(DB_PATH))
const dictDb = new Database(path.resolve(DICT_DB_PATH), { readonly: true })

// User settings DB lives in a Settings/ sub-folder next to the main DB
const userSettingsDbPath = path.join(path.dirname(path.resolve(DB_PATH)), 'Settings', 'user_settings.db')
let userSettingsDb
try {
  userSettingsDb = new Database(userSettingsDbPath)
  userSettingsDb.exec(`
    CREATE TABLE IF NOT EXISTS user_highlights (
      id          INTEGER PRIMARY KEY AUTOINCREMENT,
      bookId      INTEGER NOT NULL,
      lineId      INTEGER NOT NULL,
      startOffset INTEGER NOT NULL,
      endOffset   INTEGER NOT NULL,
      colorArgb   INTEGER NOT NULL,
      createdAt   INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line ON user_highlights (bookId, lineId);
    CREATE TABLE IF NOT EXISTS user_notes (
      id          INTEGER PRIMARY KEY AUTOINCREMENT,
      bookId      INTEGER NOT NULL,
      lineId      INTEGER NOT NULL,
      startOffset INTEGER NOT NULL,
      endOffset   INTEGER NOT NULL,
      note        TEXT    NOT NULL,
      quote       TEXT    NOT NULL,
      createdAt   INTEGER NOT NULL,
      updatedAt   INTEGER NOT NULL
    );
    CREATE INDEX IF NOT EXISTS idx_user_notes_book_line ON user_notes (bookId, lineId);
  `)
  console.log(`User settings DB: ${userSettingsDbPath}`)
} catch (err) {
  console.warn(`Could not open user settings DB at ${userSettingsDbPath}: ${err.message}`)
  userSettingsDb = null
}

const QUERY_ROUTES = new Set(['/query', '/query-dict', '/query-user-settings'])
const EXECUTE_ROUTES = new Set(['/execute-user-settings'])

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*')
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type')

  if (req.method === 'OPTIONS') {
    res.writeHead(204)
    res.end()
    return
  }

  const isQuery = req.method === 'POST' && QUERY_ROUTES.has(req.url)
  const isExecute = req.method === 'POST' && EXECUTE_ROUTES.has(req.url)

  if (!isQuery && !isExecute) {
    res.writeHead(404)
    res.end()
    return
  }

  let body = ''
  req.on('data', chunk => (body += chunk))
  req.on('end', () => {
    try {
      const { sql, params = [] } = JSON.parse(body)

      if (req.url === '/execute-user-settings') {
        if (!userSettingsDb) {
          res.writeHead(503, { 'Content-Type': 'application/json' })
          res.end(JSON.stringify({ error: 'User settings DB not available' }))
          return
        }
        const statement = userSettingsDb.prepare(sql)
        const result = statement.run(...params)
        res.writeHead(200, { 'Content-Type': 'application/json' })
        res.end(JSON.stringify({ lastInsertId: result.lastInsertRowid }))
        return
      }

      const target =
        req.url === '/query-dict' ? dictDb :
        req.url === '/query-user-settings' ? userSettingsDb :
        db

      if (!target) {
        res.writeHead(503, { 'Content-Type': 'application/json' })
        res.end(JSON.stringify({ error: 'Database not available' }))
        return
      }

      const rows = target.prepare(sql).all(...params)
      res.writeHead(200, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ rows }))
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ error: err.message }))
    }
  })
})

server.listen(PORT, () => console.log(`SQLite server running on http://localhost:${PORT}`))
