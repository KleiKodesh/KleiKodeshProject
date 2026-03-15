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

const DB_PATH = process.env.DB_PATH ?? './data.db'
const PORT = process.env.PORT ?? 4000

const db = new Database(path.resolve(DB_PATH))

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*')
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type')

  if (req.method === 'OPTIONS') {
    res.writeHead(204)
    res.end()
    return
  }

  if (req.method !== 'POST' || req.url !== '/query') {
    res.writeHead(404)
    res.end()
    return
  }

  let body = ''
  req.on('data', chunk => (body += chunk))
  req.on('end', () => {
    try {
      const { sql, params = [] } = JSON.parse(body)
      const rows = db.prepare(sql).all(...params)
      res.writeHead(200, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ rows }))
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' })
      res.end(JSON.stringify({ error: err.message }))
    }
  })
})

server.listen(PORT, () => console.log(`SQLite server running on http://localhost:${PORT}`))
