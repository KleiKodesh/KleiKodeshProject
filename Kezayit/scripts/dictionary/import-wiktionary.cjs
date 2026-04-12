/**
 * import-wiktionary.cjs
 *
 * Fetches Hebrew word entries from the Hebrew Wiktionary API and imports
 * them into dictionary.db as source 25.
 *
 * Format discovered:
 *   ==אַבָּא==                     ← nikud in single == header
 *   #{{רובד|חזל}} definition       ← definition lines (strip templates)
 *   ===מילים נרדפות===
 *   * [[synonym]]                  ← synonyms
 *
 * Resume: progress saved in meta key 'wiktionary_apcontinue'.
 * Run again to resume from where it left off.
 *
 * License: CC BY-SA 4.0
 *
 * Usage: node scripts/dictionary/import-wiktionary.cjs
 */

const https    = require('https')
const Database = require('better-sqlite3')
const path     = require('path')

const DICT_DB = path.resolve(__dirname, '../../public/dictionary.db')
const API     = 'https://he.wiktionary.org/w/api.php'
const SOURCE  = 25
const BATCH   = 10    // pages per content request
const DELAY   = 500   // ms between requests

// ── DB setup ──────────────────────────────────────────────────────────────────
const db = new Database(DICT_DB)
db.exec(`PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;`)

const ins = db.prepare(`
  INSERT OR IGNORE INTO entry (headword, nikud, definition, type, source, bookId, lineIndex)
  VALUES (?, ?, ?, 'wiktionary', ${SOURCE}, NULL, NULL)
`)

// ── Helpers ───────────────────────────────────────────────────────────────────

function get(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { 'User-Agent': 'KezayitDictBot/1.0' } }, res => {
      let data = ''
      res.on('data', c => data += c)
      res.on('end', () => {
        try { resolve(JSON.parse(data)) }
        catch (e) { reject(new Error('JSON parse error: ' + data.substring(0, 100))) }
      })
    }).on('error', reject)
  })
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)) }

function post(url, body) {
  return new Promise((resolve, reject) => {
    const postData = body
    const urlObj = new URL(url)
    const options = {
      hostname: urlObj.hostname,
      path: urlObj.pathname,
      method: 'POST',
      headers: {
        'User-Agent': 'KezayitDictBot/1.0',
        'Content-Type': 'application/x-www-form-urlencoded',
        'Content-Length': Buffer.byteLength(postData),
      },
    }
    const req = https.request(options, res => {
      let data = ''
      res.on('data', c => data += c)
      res.on('end', () => {
        try { resolve(JSON.parse(data)) }
        catch (e) { reject(new Error('JSON parse error')) }
      })
    })
    req.on('error', reject)
    req.write(postData)
    req.end()
  })
}

function stripNikud(s) {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

/** Clean wikitext: strip templates, links, markup */
function cleanWiki(s) {
  return s
    .replace(/\{\{[^}]*\}\}/g, '')           // {{templates}}
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2') // [[link|text]] → text
    .replace(/'{2,3}/g, '')                   // bold/italic
    .replace(/<[^>]+>/g, '')                  // HTML tags
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Parse a single wikitext page.
 * A page may have multiple word senses (multiple ==word== sections).
 * Returns array of { nikud, definition, synonyms[] }
 */
function parsePage(title, wikitext) {
  if (!wikitext || !containsHebrew(title)) return []

  const results = []
  const lines = wikitext.split('\n')

  let currentNikud = null
  let currentDefs = []
  let currentSyns = []
  let inSynSection = false

  function flush() {
    if (currentDefs.length === 0) return
    results.push({
      nikud: currentNikud,
      definition: currentDefs.join(' *** '),
      synonyms: [...currentSyns],
    })
  }

  for (const line of lines) {
    // New word sense: ==אַבָּא== or ==בָּרָא {{משני|א}}==
    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flush()
      currentDefs = []
      currentSyns = []
      inSynSection = false
      // Extract nikud from the header
      const raw = senseMatch[1].replace(/\{\{[^}]*\}\}/g, '').trim()
      currentNikud = /[\u05B0-\u05C7]/.test(raw) ? raw : null
      continue
    }

    // Section headers
    if (/^===מילים נרדפות===/.test(line)) { inSynSection = true; continue }
    if (/^===/.test(line)) { inSynSection = false; continue }

    // Definition line: starts with # but not #: (examples) or #* (bullets)
    if (/^#{1,2}[^:#*]/.test(line)) {
      const def = cleanWiki(line.replace(/^#+\s*/, ''))
      if (def && def.length > 1 && def.length < 400) currentDefs.push(def)
      continue
    }

    // Synonym line
    if (inSynSection && /^\*/.test(line)) {
      // May have multiple synonyms: * [[word1]], [[word2]] (1,2)
      const synLine = line.replace(/^\*+\s*/, '').replace(/\(\d[^)]*\)/g, '')
      const synMatches = [...synLine.matchAll(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g)]
      for (const m of synMatches) {
        const syn = m[2].replace(/[#|].*/, '').trim()
        if (syn && containsHebrew(syn) && syn.length < 30) currentSyns.push(syn)
      }
      // Also plain text synonyms (no brackets)
      if (synMatches.length === 0) {
        const plain = cleanWiki(synLine)
        if (plain && containsHebrew(plain) && plain.length < 30) currentSyns.push(plain)
      }
    }
  }

  flush()
  return results
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  let apcontinue = db.prepare(`SELECT value FROM meta WHERE key = 'wiktionary_apcontinue'`).get()?.value ?? ''
  const existing = db.prepare(`SELECT COUNT(*) as cnt FROM entry WHERE source = ${SOURCE}`).get().cnt

  console.log(`Wiktionary import (source ${SOURCE})`)
  console.log(`Resume: "${apcontinue || 'beginning'}" | Already imported: ${existing}`)

  let totalPages = 0
  let totalImported = 0
  let batchNum = 0

  const insertBatch = db.transaction((rows) => {
    for (const r of rows) ins.run(r.headword, r.nikud, r.definition)
  })

  let apfrom = apcontinue  // use apfrom for resume (stable page title)
  let apcontToken = ''     // use apcontinue token within a session

  while (true) {
    await sleep(DELAY)
    // Use apfrom for initial/resume, apcontinue token for subsequent pages
    const contParam = apcontToken
      ? `&apcontinue=${encodeURIComponent(apcontToken)}`
      : `&apfrom=${encodeURIComponent(apfrom)}`
    const listUrl = `${API}?action=query&list=allpages&apnamespace=0&aplimit=${BATCH}${contParam}&format=json`

    let listData
    try { listData = await get(listUrl) }
    catch (e) { 
      console.error('\nList error:', e.message)
      await sleep(5000)
      continue
    }
    if (!listData?.query) {
      console.error('\nInvalid response, retrying...')
      await sleep(5000)
      continue
    }

    const pages = listData?.query?.allpages || []
    if (pages.length === 0) { console.log('\nNo pages returned for:', apcontinue); break }

    // Filter to Hebrew-titled pages only
    const hePages = pages.filter(p => containsHebrew(p.title))
    if (hePages.length === 0) {
      const next = listData?.continue?.apcontinue || ''
      process.stdout.write(`\r  Skipping non-Hebrew batch, next="${next.substring(0,15)}"`)
      if (!next) break
      apcontToken = next
      apfrom = ''
      db.prepare(`INSERT OR REPLACE INTO meta (key,value) VALUES ('wiktionary_apcontinue',?)`).run(
        pages[pages.length - 1]?.title || next
      )
      continue
    }

    // 2. Fetch wikitext for this batch via POST
    await sleep(DELAY)
    const titles = hePages.map(p => p.title).join('|')
    const postBody = `action=query&titles=${encodeURIComponent(titles)}&prop=revisions&rvprop=content&rvslots=main&format=json`
    let contentData
    try { contentData = await post(API, postBody) }
    catch (e) { console.error('\nContent error:', e.message); await sleep(2000); continue }

    const pageMap = contentData?.query?.pages || {}
    const entries = []

    for (const p of Object.values(pageMap)) {
      if (!p.title || !containsHebrew(p.title)) continue
      const wikitext = p.revisions?.[0]?.slots?.main?.['*'] || p.revisions?.[0]?.['*'] || ''
      const senses = parsePage(p.title, wikitext)

      for (const sense of senses) {
        const headword = stripNikud(p.title)
        if (!headword || !containsHebrew(headword)) continue
        entries.push({ headword, nikud: sense.nikud, definition: sense.definition })

        // Add synonyms as separate entries pointing back
        for (const syn of sense.synonyms) {
          const synHead = stripNikud(syn)
          if (synHead && containsHebrew(synHead) && synHead !== headword) {
            entries.push({
              headword: synHead,
              nikud: /[\u05B0-\u05C7]/.test(syn) ? syn : null,
              definition: `ראה גם: ${headword}`,
            })
          }
        }
      }
    }

    if (entries.length > 0) {
      insertBatch(entries)
      totalImported += entries.length
    }

    totalPages += hePages.length
    batchNum++

    const next = listData?.continue?.apcontinue || ''
    apcontToken = next
    apfrom = ''  // once we have a continue token, use that
    db.prepare(`INSERT OR REPLACE INTO meta (key,value) VALUES ('wiktionary_apcontinue',?)`).run(
      pages[pages.length - 1]?.title || next  // save last page title as stable resume point
    )

    process.stdout.write(`\r  Batch ${batchNum}: ${totalPages} pages, ${totalImported} entries | next="${next.substring(0,12)}"`)

    if (!next) break
  }

  console.log(`\n\nDone! Imported ${totalImported} entries from Wiktionary.`)
  db.prepare(`INSERT OR REPLACE INTO meta (key,value) VALUES ('wiktionary_done','1')`).run()
  db.close()
}

main().catch(e => { console.error('\nFatal:', e.message, e.stack); process.exit(1) })
