'use strict'
/**
 * add-netfree-flag.cjs
 *
 * Adds a `netfree_blocked` INTEGER column (0/1) to the `sense` table in
 * wikidictionary.db, then queries each distinct headword against he.wiktionary.org
 * through the NetFree filter to detect which words are blocked.
 *
 * Detection: NetFree intercepts the request and returns HTTP 418 with a block
 * page containing "netfree.link/block/". A normal response is HTTP 200.
 *
 * Safety check: if >20% of words are blocked we abort — this catches the case
 * where something is wrong (e.g. wiktionary is down, or all requests are failing).
 *
 * Usage:   node scripts/dictionary/add-netfree-flag.cjs
 * Options: CONCURRENCY=10   parallel requests (default 10)
 *          DRY_RUN=1        print stats without writing to DB
 *          RESUME=1         skip headwords already flagged (netfree_blocked != 0 treated as done)
 */

const Database = require('better-sqlite3')
const https = require('https')
const path = require('path')

const DB_PATH = path.resolve(__dirname, '../../public/dicts/wikidictionary.db')
const CONCURRENCY = parseInt(process.env.CONCURRENCY || '10', 10)
const DRY_RUN = process.env.DRY_RUN === '1'
const RESUME = process.env.RESUME === '1'
const MAX_BLOCKED_RATIO = 0.20
const USER_AGENT = 'Mozilla/5.0 KezayitDict/1.0'
const BASE_URL = 'https://milog.co.il/'

// ── NetFree check ─────────────────────────────────────────────────────────────
// Returns 'blocked' | 'clean' | 'error'
function checkWord(word) {
  return new Promise((resolve) => {
    const url = BASE_URL + encodeURIComponent(word)
    const req = https.request(url, { timeout: 10000, headers: { 'User-Agent': USER_AGENT } }, (res) => {
      // Drain the body so the socket is released
      res.resume()
      if (res.statusCode === 418) return resolve('blocked')
      if (res.statusCode === 200) return resolve('clean')
      // Any other status (404 = word not in wiktionary, etc.) — not a NetFree block
      resolve('clean')
    })
    req.on('error', () => resolve('error'))
    req.on('timeout', () => { req.destroy(); resolve('error') })
    req.end()
  })
}

// ── Concurrency pool ──────────────────────────────────────────────────────────
async function runPool(items, concurrency, fn) {
  const results = new Array(items.length)
  let idx = 0
  async function worker() {
    while (idx < items.length) {
      const i = idx++
      results[i] = await fn(items[i], i)
    }
  }
  await Promise.all(Array.from({ length: concurrency }, worker))
  return results
}

// ── Main ──────────────────────────────────────────────────────────────────────
async function main() {
  const db = new Database(DB_PATH)

  // Add column if missing
  const cols = db.prepare('PRAGMA table_info(sense)').all().map((c) => c.name)
  if (!cols.includes('netfree_blocked')) {
    console.log('Adding netfree_blocked column...')
    db.prepare('ALTER TABLE sense ADD COLUMN netfree_blocked INTEGER NOT NULL DEFAULT 0').run()
  }

  // Get headwords — optionally skip already-processed ones
  let rows
  if (RESUME) {
    // NULL means never checked (default 0 was set by ALTER, so we can't distinguish)
    // For resume support we treat 0 as "not yet checked" only if the column was just added.
    // Simplest: just re-check everything unless user explicitly set RESUME=1 after a partial run.
    rows = db.prepare('SELECT DISTINCT headword FROM sense WHERE netfree_blocked = 0 ORDER BY headword').all()
    console.log(`RESUME mode: ${rows.length} unchecked headwords remaining`)
  } else {
    rows = db.prepare('SELECT DISTINCT headword FROM sense ORDER BY headword').all()
    console.log(`Checking all ${rows.length} distinct headwords (concurrency=${CONCURRENCY})...`)
  }

  const headwords = rows.map((r) => r.headword)
  if (headwords.length === 0) { console.log('Nothing to check.'); db.close(); return }

  let checked = 0
  let blocked = 0
  let errors = 0

  const results = await runPool(headwords, CONCURRENCY, async (word) => {
    const result = await checkWord(word)
    checked++
    if (result === 'blocked') blocked++
    if (result === 'error') errors++

    if (checked % 50 === 0 || checked === headwords.length) {
      const pct = ((blocked / checked) * 100).toFixed(1)
      process.stdout.write(`\r  ${checked}/${headwords.length} — ${blocked} blocked (${pct}%), ${errors} errors   `)
    }
    return result
  })

  console.log(`\n\nResults: ${blocked} blocked, ${headwords.length - blocked - errors} clean, ${errors} errors`)

  // Safety check — abort if something looks wrong
  const checkedOk = checked - errors
  if (checkedOk > 50 && blocked / checkedOk > MAX_BLOCKED_RATIO) {
    console.error(`\nABORTING: ${(blocked / checkedOk * 100).toFixed(1)}% of successful checks are blocked.`)
    console.error(`This exceeds the ${MAX_BLOCKED_RATIO * 100}% safety threshold — something is likely wrong.`)
    console.error('No changes written to DB. Run with DRY_RUN=1 to inspect.')
    db.close()
    process.exit(1)
  }

  if (DRY_RUN) {
    const blockedWords = headwords.filter((_, i) => results[i] === 'blocked')
    console.log('DRY_RUN=1 — not writing to DB.')
    console.log('Blocked words sample:', blockedWords.slice(0, 30))
    db.close()
    return
  }

  // Write in a single transaction
  console.log('Writing to DB...')
  const update = db.prepare('UPDATE sense SET netfree_blocked = ? WHERE headword = ?')
  db.transaction(() => {
    for (let i = 0; i < headwords.length; i++) {
      update.run(results[i] === 'blocked' ? 1 : 0, headwords[i])
    }
  })()

  const blockedWords = headwords.filter((_, i) => results[i] === 'blocked')
  console.log(`Done. Flagged ${blocked} headwords as blocked.`)
  if (blockedWords.length > 0) console.log('Blocked words:', blockedWords)
  db.close()
}

main().catch((err) => { console.error(err); process.exit(1) })
