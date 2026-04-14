'use strict'
/**
 * netfree-xml-check.cjs
 *
 * Fetches each headword from wikidictionary.db via Special:Export through
 * the current network (NetFree). If NetFree blocks the page, the XML response
 * will contain no <text> content or an empty/redirect page — we flag it.
 *
 * This is the accurate method: the same mechanism used during the original
 * import, where NetFree silently dropped inappropriate pages.
 *
 * Updates netfree_blocked = 1 for headwords whose export comes back empty.
 *
 * Usage:   node scripts/dictionary/netfree-xml-check.cjs
 * Options: CONCURRENCY=5  (default 5 — be gentle with the export endpoint)
 *          DRY_RUN=1      (print stats without writing to DB)
 *          BATCH=20       (titles per export request, default 20)
 */

const https    = require('https')
const path     = require('path')
const Database = require('better-sqlite3')

const DB_PATH     = path.resolve(__dirname, '../../public/dicts/wikidictionary.db')
const EXPORT_URL  = 'https://he.wiktionary.org/wiki/%D7%9E%D7%99%D7%95%D7%97%D7%93:%D7%99%D7%99%D7%A6%D7%95%D7%90'
const CONCURRENCY = parseInt(process.env.CONCURRENCY || '5', 10)
const BATCH_SIZE  = parseInt(process.env.BATCH || '20', 10)
const DRY_RUN     = process.env.DRY_RUN === '1'
const DELAY       = 400 // ms between requests
const MAX_BLOCKED_RATIO = 0.20

function sleep(ms) { return new Promise(r => setTimeout(r, ms)) }

// ── POST to Special:Export ────────────────────────────────────────────────────
function exportPages(titles) {
  return new Promise((resolve, reject) => {
    const postData = new URLSearchParams({
      action: 'submit',
      pages: titles.join('\n'),
      curonly: '1',
      wpDownload: '0',
    }).toString()

    const urlObj = new URL(EXPORT_URL)
    const options = {
      hostname: urlObj.hostname,
      path: urlObj.pathname,
      method: 'POST',
      timeout: 20000,
      headers: {
        'User-Agent': 'KezayitDictBot/1.0',
        'Content-Type': 'application/x-www-form-urlencoded',
        'Content-Length': Buffer.byteLength(postData),
      },
    }

    const req = https.request(options, (res) => {
      let body = ''
      res.on('data', c => (body += c))
      res.on('end', () => resolve({ status: res.statusCode, body }))
    })
    req.on('error', reject)
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')) })
    req.write(postData)
    req.end()
  })
}

// ── Parse which titles came back with real content ────────────────────────────
// Returns two sets: present (has content) and notFound (page doesn't exist on wiki)
// Missing from both = NetFree blocked the response
function getTitlesWithContent(xml, requestedTitles) {
  const present = new Set()
  const notFound = new Set()

  // Pages that don't exist come back with <text bytes="-1" /> or missing text
  const pageRe = /<page>([\s\S]*?)<\/page>/g
  let m
  while ((m = pageRe.exec(xml)) !== null) {
    const pageXml = m[1]
    const titleMatch = pageXml.match(/<title>([^<]+)<\/title>/)
    if (!titleMatch) continue
    const title = titleMatch[1].replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').trim()

    // bytes="-1" means page doesn't exist
    const missingMatch = pageXml.match(/<text\s[^>]*bytes="-1"/)
    if (missingMatch) { notFound.add(title); continue }

    const textMatch = pageXml.match(/<text[^>]*>([\s\S]*?)<\/text>/)
    const text = textMatch ? textMatch[1].trim() : ''
    const isRedirect = /^#הפניה|^#REDIRECT/i.test(text)
    const isEmpty = text.length < 10

    if (!isRedirect && !isEmpty) present.add(title)
    else notFound.add(title) // redirect or empty = not a real entry
  }

  // Titles completely absent from the XML = NetFree stripped the page
  const netfreeBlocked = new Set()
  for (const t of requestedTitles) {
    if (!present.has(t) && !notFound.has(t)) netfreeBlocked.add(t)
  }

  return { present, notFound, netfreeBlocked }
}

// ── Concurrency pool ──────────────────────────────────────────────────────────
async function runPool(batches, concurrency, fn) {
  const results = []
  let idx = 0
  async function worker() {
    while (idx < batches.length) {
      const i = idx++
      results[i] = await fn(batches[i], i)
      await sleep(DELAY)
    }
  }
  await Promise.all(Array.from({ length: concurrency }, worker))
  return results.flat()
}

// ── Main ──────────────────────────────────────────────────────────────────────
async function main() {
  const db = new Database(DB_PATH)

  // Ensure column exists
  const cols = db.prepare('PRAGMA table_info(sense)').all().map(c => c.name)
  if (!cols.includes('netfree_blocked')) {
    db.prepare('ALTER TABLE sense ADD COLUMN netfree_blocked INTEGER NOT NULL DEFAULT 0').run()
    console.log('Added netfree_blocked column')
  }

  // Get all distinct headwords
  const headwords = db.prepare('SELECT DISTINCT headword FROM sense ORDER BY headword').all().map(r => r.headword)
  console.log(`Checking ${headwords.length} headwords via Special:Export (batch=${BATCH_SIZE}, concurrency=${CONCURRENCY})...`)

  // Split into batches
  const batches = []
  for (let i = 0; i < headwords.length; i += BATCH_SIZE) {
    batches.push(headwords.slice(i, i + BATCH_SIZE))
  }

  let checked = 0
  let blocked = 0
  let errors = 0
  const blockedSet = new Set()

  // For each batch: export → see which titles came back → missing = blocked
  const allResults = await runPool(batches, CONCURRENCY, async (batch, batchIdx) => {
    let res
    try {
      res = await exportPages(batch)
    } catch (e) {
      // On error (e.g. NetFree blocked the entire request), retry once
      await sleep(2000)
      try { res = await exportPages(batch) }
      catch (e2) {
        errors += batch.length
        checked += batch.length
        return batch.map(w => ({ word: w, blocked: false, error: true }))
      }
    }

    // If NetFree blocked the export endpoint itself (418), all words in batch are blocked
    if (res.status === 418 || res.body.includes('netfree.link/block')) {
      blocked += batch.length
      checked += batch.length
      batch.forEach(w => blockedSet.add(w))
      process.stdout.write(`\r  Batch ${batchIdx+1}/${batches.length} — ${checked}/${headwords.length} checked, ${blocked} blocked, ${errors} errors   `)
      return batch.map(w => ({ word: w, blocked: true }))
    }

    const { present, netfreeBlocked: batchBlocked } = getTitlesWithContent(res.body, batch)
    const batchResults = batch.map(w => {
      const isBlocked = batchBlocked.has(w)
      if (isBlocked) { blocked++; blockedSet.add(w) }
      checked++
      return { word: w, blocked: isBlocked }
    })

    process.stdout.write(`\r  Batch ${batchIdx+1}/${batches.length} — ${checked}/${headwords.length} checked, ${blocked} blocked, ${errors} errors   `)
    return batchResults
  })

  console.log(`\n\nResults: ${blocked} blocked, ${headwords.length - blocked - errors} clean, ${errors} errors`)

  // Safety check
  const checkedOk = checked - errors
  if (checkedOk > 100 && blocked / checkedOk > MAX_BLOCKED_RATIO) {
    console.error(`\nABORTING: ${(blocked/checkedOk*100).toFixed(1)}% blocked exceeds ${MAX_BLOCKED_RATIO*100}% safety threshold.`)
    console.error('No changes written.')
    db.close()
    process.exit(1)
  }

  if (DRY_RUN) {
    console.log('DRY_RUN=1 — not writing to DB.')
    console.log('Blocked words:', [...blockedSet].slice(0, 50))
    db.close()
    return
  }

  console.log('Writing to DB...')
  const update = db.prepare('UPDATE sense SET netfree_blocked = ? WHERE headword = ?')
  db.transaction(() => {
    for (const { word, blocked: isBlocked } of allResults) {
      update.run(isBlocked ? 1 : 0, word)
    }
  })()

  console.log(`Done. ${blocked} headwords flagged as netfree_blocked.`)
  if (blockedSet.size > 0) console.log('Blocked words:', [...blockedSet])
  db.close()
}

main().catch(e => { console.error('\nFatal:', e.message); process.exit(1) })
