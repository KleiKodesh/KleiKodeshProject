/**
 * import-wiktionary-export.cjs
 *
 * Uses the Wiktionary Special:Export endpoint (which is not blocked)
 * to fetch all Hebrew word pages and import them into dictionary.db.
 *
 * Strategy:
 *   1. Use allpages API to get titles (if available) OR
 *      iterate through the alphabet using apfrom
 *   2. Export pages in batches via Special:Export (POST with list of titles)
 *   3. Parse the XML wikitext and extract definitions + synonyms
 *
 * Resume: saves progress in meta key 'wiktionary_export_from'
 *
 * Usage: node scripts/dictionary/import-wiktionary-export.cjs
 */

const https    = require('https')
const http     = require('http')
const Database = require('better-sqlite3')
const path     = require('path')

const DICT_DB    = path.resolve(__dirname, '../../public/dictionary.db')
const EXPORT_URL = 'https://he.wiktionary.org/wiki/%D7%9E%D7%99%D7%95%D7%97%D7%93:%D7%99%D7%99%D7%A6%D7%95%D7%90'
const API_URL    = 'https://he.wiktionary.org/w/api.php'
const SOURCE     = 25
const BATCH      = 20
const DELAY      = 600

// ── DB ────────────────────────────────────────────────────────────────────────
const db = new Database(DICT_DB)
db.exec(`PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;`)

const ins = db.prepare(`
  INSERT OR IGNORE INTO entry (headword, nikud, definition, type, source, bookId, lineIndex)
  VALUES (?, ?, ?, 'wiktionary', ${SOURCE}, NULL, NULL)
`)

const insertBatch = db.transaction((rows) => {
  for (const r of rows) ins.run(r.headword, r.nikud, r.definition)
})

// ── Helpers ───────────────────────────────────────────────────────────────────

function sleep(ms) { return new Promise(r => setTimeout(r, ms)) }

function stripNikud(s) {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

function cleanWiki(s) {
  return s
    .replace(/\{\{[^}]*\}\}/g, '')
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')
    .replace(/'{2,3}/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

/** GET request */
function get(url) {
  return new Promise((resolve, reject) => {
    const mod = url.startsWith('https') ? https : http
    mod.get(url, { headers: { 'User-Agent': 'KezayitDictBot/1.0' } }, res => {
      let data = ''
      res.on('data', c => data += c)
      res.on('end', () => resolve({ status: res.statusCode, body: data }))
    }).on('error', reject)
  })
}

/** POST request */
function post(url, body) {
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url)
    const postData = typeof body === 'string' ? body : new URLSearchParams(body).toString()
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
      res.on('end', () => resolve({ status: res.statusCode, body: data }))
    })
    req.on('error', reject)
    req.write(postData)
    req.end()
  })
}

/** Parse wikitext into senses */
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
    results.push({ nikud: currentNikud, definition: currentDefs.join(' *** '), synonyms: [...currentSyns] })
  }

  for (const line of lines) {
    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flush(); currentDefs = []; currentSyns = []; inSynSection = false
      const raw = senseMatch[1].replace(/\{\{[^}]*\}\}/g, '').trim()
      currentNikud = /[\u05B0-\u05C7]/.test(raw) ? raw : null
      continue
    }
    if (/^===מילים נרדפות===/.test(line)) { inSynSection = true; continue }
    if (/^===/.test(line)) { inSynSection = false; continue }

    if (/^#{1,2}[^:#*]/.test(line)) {
      const def = cleanWiki(line.replace(/^#+\s*/, ''))
      if (def && def.length > 1 && def.length < 400) currentDefs.push(def)
    }
    if (inSynSection && /^\*/.test(line)) {
      const synMatches = [...line.matchAll(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g)]
      for (const m of synMatches) {
        const syn = m[2].replace(/[#|].*/, '').trim()
        if (syn && containsHebrew(syn) && syn.length < 30) currentSyns.push(syn)
      }
    }
  }
  flush()
  return results
}

/** Extract wikitext from XML export response */
function extractFromXml(xml) {
  const pages = []
  const pageRegex = /<page>([\s\S]*?)<\/page>/g
  let m
  while ((m = pageRegex.exec(xml)) !== null) {
    const pageXml = m[1]
    const titleMatch = pageXml.match(/<title>([^<]+)<\/title>/)
    const textMatch  = pageXml.match(/<text[^>]*>([\s\S]*?)<\/text>/)
    if (titleMatch && textMatch) {
      const title = titleMatch[1].replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"')
      const text  = textMatch[1].replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#039;/g, "'")
      pages.push({ title, text })
    }
  }
  return pages
}

/** Fetch a batch of pages via Special:Export */
async function exportPages(titles) {
  const body = new URLSearchParams({
    action: 'submit',
    pages: titles.join('\n'),
    curonly: '1',
    wpDownload: '0',
  }).toString()

  const res = await post(EXPORT_URL, body)
  if (res.status !== 200) throw new Error(`Export HTTP ${res.status}`)
  return extractFromXml(res.body)
}

/** Get a batch of page titles via API */
async function getTitles(apfrom, limit) {
  const url = `${API_URL}?action=query&list=allpages&apnamespace=0&aplimit=${limit}&apfrom=${encodeURIComponent(apfrom)}&format=json`
  const res = await get(url)
  if (res.status !== 200) return null
  try {
    const j = JSON.parse(res.body)
    return {
      titles: (j.query?.allpages || []).map(p => p.title),
      next: j.continue?.apcontinue || '',
    }
  } catch { return null }
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  let apfrom = db.prepare(`SELECT value FROM meta WHERE key='wiktionary_export_from'`).get()?.value ?? ''
  const existing = db.prepare(`SELECT COUNT(*) as c FROM entry WHERE source=${SOURCE}`).get().c

  console.log(`Wiktionary export import (source ${SOURCE})`)
  console.log(`Resume from: "${apfrom || 'beginning'}" | Already: ${existing} entries`)

  let totalImported = 0
  let batchNum = 0
  let apiAvailable = true

  // Hebrew alphabet for fallback iteration when API is blocked
  const ALEPH_BET = ['א','ב','ג','ד','ה','ו','ז','ח','ט','י','כ','ל','מ','נ','ס','ע','פ','צ','ק','ר','ש','ת']

  // Determine starting letter for fallback
  let startLetterIdx = 0
  if (apfrom) {
    const firstChar = apfrom[0]
    const idx = ALEPH_BET.indexOf(firstChar)
    if (idx >= 0) startLetterIdx = idx
  }

  while (true) {
    let titles = []
    let nextApfrom = ''

    // Try API first
    if (apiAvailable) {
      await sleep(DELAY)
      const result = await getTitles(apfrom, BATCH)
      if (result && result.titles.length > 0) {
        titles = result.titles.filter(t => containsHebrew(t))
        nextApfrom = result.next
        if (!result.next) apiAvailable = false // reached end or blocked
      } else {
        console.log('\nAPI unavailable, switching to alphabet iteration...')
        apiAvailable = false
      }
    }

    // Fallback: iterate by letter
    if (!apiAvailable && titles.length === 0) {
      if (startLetterIdx >= ALEPH_BET.length) break
      const letter = ALEPH_BET[startLetterIdx]
      console.log(`\nFetching letter: ${letter}`)

      // Use the export to get all pages starting with this letter
      // We'll use the API's allpages with apfrom=letter and aplimit=500
      await sleep(DELAY)
      const url = `${API_URL}?action=query&list=allpages&apnamespace=0&aplimit=500&apfrom=${encodeURIComponent(letter)}&format=json`
      const res = await get(url)
      if (res.status === 200) {
        try {
          const j = JSON.parse(res.body)
          const allTitles = (j.query?.allpages || []).map(p => p.title)
          // Keep only titles starting with this letter
          titles = allTitles.filter(t => containsHebrew(t) && t[0] === letter)
          startLetterIdx++
          nextApfrom = ALEPH_BET[startLetterIdx] || ''
        } catch {
          startLetterIdx++
          continue
        }
      } else {
        startLetterIdx++
        await sleep(2000)
        continue
      }
    }

    if (titles.length === 0) {
      if (apiAvailable) { apfrom = nextApfrom; continue }
      break
    }

    // Export in sub-batches of 20
    for (let i = 0; i < titles.length; i += BATCH) {
      const batch = titles.slice(i, i + BATCH)
      await sleep(DELAY)

      let pages
      try { pages = await exportPages(batch) }
      catch (e) { console.error('\nExport error:', e.message); await sleep(3000); continue }

      const entries = []
      for (const { title, text } of pages) {
        if (!containsHebrew(title)) continue
        const senses = parsePage(title, text)
        for (const sense of senses) {
          const headword = stripNikud(title)
          if (!headword || !containsHebrew(headword)) continue
          entries.push({ headword, nikud: sense.nikud, definition: sense.definition })
          for (const syn of sense.synonyms) {
            const synHead = stripNikud(syn)
            if (synHead && containsHebrew(synHead) && synHead !== headword) {
              entries.push({ headword: synHead, nikud: null, definition: `ראה גם: ${headword}` })
            }
          }
        }
      }

      if (entries.length > 0) { insertBatch(entries); totalImported += entries.length }
      batchNum++
      process.stdout.write(`\r  Batch ${batchNum}: ${totalImported} entries imported | last="${batch[batch.length-1]?.substring(0,12)}"`)
    }

    // Save progress
    apfrom = nextApfrom
    db.prepare(`INSERT OR REPLACE INTO meta (key,value) VALUES ('wiktionary_export_from',?)`).run(apfrom)

    if (!apfrom && !apiAvailable) break
  }

  console.log(`\n\nDone! Total imported: ${totalImported}`)
  db.prepare(`INSERT OR REPLACE INTO meta (key,value) VALUES ('wiktionary_done','1')`).run()
  db.close()
}

main().catch(e => { console.error('\nFatal:', e.message); process.exit(1) })
