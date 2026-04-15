'use strict'
/**
 * import-wiktionary.cjs
 *
 * Adapted from old/scripts_dictionary/import-wiktionary-export.cjs — the
 * version that was confirmed working on this network.
 *
 * Uses Special:Export (POST) to fetch wikitext in batches of 20, with an
 * alphabet-iteration fallback when the allpages API is blocked.
 * Writes into public/wikidictionary.db (our normalized schema with filter_tag).
 * Resumable — re-run after interruption.
 *
 * Usage: node scripts/import-wiktionary.cjs
 */

const https    = require('https')
const http     = require('http')
const path     = require('path')
const Database = require('better-sqlite3')

const DST_DB     = path.resolve(__dirname, '../../public/dicts/wikidictionary.db')
const EXPORT_URL = 'https://he.wiktionary.org/wiki/%D7%9E%D7%99%D7%95%D7%97%D7%93:%D7%99%D7%99%D7%A6%D7%95%D7%90'
const API_URL    = 'https://he.wiktionary.org/w/api.php'
const BATCH      = 20
const DELAY      = 600

// ── HTTP helpers (same as the old working script) ─────────────────────────────

function sleep(ms) { return new Promise(r => setTimeout(r, ms)) }

function get(url) {
  return new Promise((resolve, reject) => {
    const mod = url.startsWith('https') ? https : http
    mod.get(url, { headers: { 'User-Agent': 'KezayitDictBot/1.0' } }, res => {
      let data = ''
      res.on('data', c => (data += c))
      res.on('end', () => resolve({ status: res.statusCode, body: data }))
    }).on('error', reject)
  })
}

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
      res.on('data', c => (data += c))
      res.on('end', () => resolve({ status: res.statusCode, body: data }))
    })
    req.on('error', reject)
    req.write(postData)
    req.end()
  })
}

// ── Wikitext parser (same as useWiktionary.ts) ────────────────────────────────

function stripNikud(s) {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

function cleanWiki(s) {
  return s
    .replace(/\{\{[^{}]*\}\}/g, '').replace(/\{\{[^{}]*\}\}/g, '')
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2')
    .replace(/'{2,3}/g, '')
    .replace(/<ref[^>]*>[\s\S]*?<\/ref>/g, '')
    .replace(/<[^>]+>/g, '')
    .replace(/\s+/g, ' ').trim()
}

function extractFromTemplate(block, key) {
  const m = block.match(new RegExp(`\\|${key}\\s*=\\s*([^\\n|]+)`))
  return m ? m[1].trim() : null
}

function extractShoresh(block) {
  const m3 = block.match(/\{\{שרש3\|([^|{}]+)\|([^|{}]+)\|([^|{}]+)/)
  if (m3) return `${m3[1].trim()}-${m3[2].trim()}-${m3[3].trim()}`
  const m1 = block.match(/\{\{שרש\|([^|{}\s]+)/)
  return m1 ? m1[1].trim() : null
}

const KNOWN_SECTIONS = new Set(['גיזרון','נגזרות','מילים נרדפות','ניגודים','צירופים','מידע נוסף','ראו גם','הערות שוליים','תרגום'])
const KEEP_LANGS = new Set(['אנגלית','ערבית','ארמית'])

function parsePage(title, wikitext) {
  if (!wikitext || !containsHebrew(title)) return []
  if (/^#הפניה|^#REDIRECT/i.test(wikitext.trim())) return []

  const lines = wikitext.split('\n')
  const senses = []
  let cur = null, curSection = null, curDefIdx = -1

  function flush() {
    if (cur && (cur.definitions.length > 0 || Object.keys(cur.sections).length > 0)) senses.push(cur)
    cur = null; curSection = null; curDefIdx = -1
  }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] || ''
    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flush()
      const rawHeader = (senseMatch[1] || '').replace(/\{\{[^}]*\}\}/g, '').trim()
      cur = {
        nikud: /[\u05B0-\u05C7]/.test(rawHeader) ? rawHeader : null,
        headword: stripNikud(rawHeader) || stripNikud(title),
        pos: null, binyan: null, shoresh: null, ktivMale: null,
        definitions: [], sections: {}, translations: [],
      }
      curSection = null; curDefIdx = -1
      continue
    }
    if (!cur) continue

    if (line.includes('{{ניתוח דקדוקי')) {
      let block = line, j = i + 1
      let depth = (line.match(/\{\{/g)||[]).length - (line.match(/\}\}/g)||[]).length
      while (j < lines.length && depth > 0) {
        const nl = lines[j] || ''
        block += '\n' + nl
        depth += (nl.match(/\{\{/g)||[]).length - (nl.match(/\}\}/g)||[]).length
        j++
      }
      i = j - 1
      cur.shoresh  = cur.shoresh  || extractShoresh(block)
      cur.binyan   = cur.binyan   || extractFromTemplate(block, 'בניין')
      cur.pos      = cur.pos      || extractFromTemplate(block, 'חלק דיבר')
      cur.ktivMale = cur.ktivMale || extractFromTemplate(block, 'כתיב מלא')
      continue
    }

    const secMatch = line.match(/^===([^=]+)===\s*$/)
    if (secMatch) {
      curSection = (secMatch[1] || '').trim(); curDefIdx = -1
      if (KNOWN_SECTIONS.has(curSection) && !cur.sections[curSection]) cur.sections[curSection] = []
      continue
    }
    if (/^====/.test(line)) { curSection = null; continue }

    if (!curSection && /^#{1,2}[^:#*]/.test(line)) {
      const layerMatch = line.match(/\{\{(?:מקרא|רובד|משלב)\|([^|}]+)/)
      const layer = layerMatch ? layerMatch[1].trim() : null
      const text = cleanWiki(line.replace(/^#+\s*/, ''))
      if (text && text.length > 1) {
        cur.definitions.push({ text, layer, examples: [] })
        curDefIdx = cur.definitions.length - 1
      }
      continue
    }

    if (!curSection && /^#[:#*]/.test(line) && curDefIdx >= 0) {
      const citMatch = line.match(/\{\{צט[^|]*\|([^|]+)\|([^|]+)\|([^|]+)\|([^|}]+)/)
      if (citMatch) {
        const def = cur.definitions[curDefIdx]
        if (def) def.examples.push({ text: cleanWiki(citMatch[1]), source: `${citMatch[2]} ${citMatch[3]}, ${citMatch[4]}` })
      }
      continue
    }

    if (curSection && cur.sections[curSection] !== undefined) {
      if (curSection === 'תרגום') {
        const langMatch = line.match(/^\*\s*([^:：]+)[：:]\s*(.+)/)
        if (langMatch && KEEP_LANGS.has(langMatch[1].trim())) {
          const words = [...(langMatch[2]||'').matchAll(/\{\{ת\|[^|]+\|([^|}]+)/g)].map(m => m[1].trim())
          if (words.length) cur.translations.push({ lang: langMatch[1].trim(), words })
        }
        continue
      }
      if (/^\*+/.test(line)) {
        const text = cleanWiki(line.replace(/^\*+\s*/, ''))
        if (text && containsHebrew(text) && text.length < 80) cur.sections[curSection].push(text)
        continue
      }
      if (line.trim() && !/^[={<[]/.test(line)) {
        const text = cleanWiki(line)
        if (text && text.length > 4) cur.sections[curSection].push(text)
      }
    }
  }
  flush()
  return senses
}

// ── XML export parser ─────────────────────────────────────────────────────────

function extractFromXml(xml) {
  const pages = []
  const pageRe = /<page>([\s\S]*?)<\/page>/g
  let m
  while ((m = pageRe.exec(xml)) !== null) {
    const pageXml = m[1]
    const titleMatch = pageXml.match(/<title>([^<]+)<\/title>/)
    const textMatch  = pageXml.match(/<text[^>]*>([\s\S]*?)<\/text>/)
    if (!titleMatch || !textMatch) continue
    const unescape = s => s.replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#039;/g,"'")
    pages.push({ title: unescape(titleMatch[1]), wikitext: unescape(textMatch[1]) })
  }
  return pages
}

async function exportPages(titles) {
  const body = new URLSearchParams({ action: 'submit', pages: titles.join('\n'), curonly: '1', wpDownload: '0' }).toString()
  const res = await post(EXPORT_URL, body)
  if (res.status !== 200) throw new Error(`Export HTTP ${res.status}`)
  return extractFromXml(res.body)
}

async function getTitles(apfrom, limit) {
  const url = `${API_URL}?action=query&list=allpages&apnamespace=0&aplimit=${limit}&apfrom=${encodeURIComponent(apfrom)}&format=json`
  const res = await get(url)
  if (res.status !== 200) return null
  try {
    const j = JSON.parse(res.body)
    return { titles: (j.query?.allpages || []).map(p => p.title), next: j.continue?.apcontinue || '' }
  } catch { return null }
}

// ── Period tag derivation ─────────────────────────────────────────────────────
// Maps raw filter_tag values to one of 4 canonical period buckets.
// NULL = modern standard Hebrew (always shown, no filtering needed).
const PERIOD_MAP = {
  // Biblical
  'המקרא': 'מקרא', 'לשון המקרא': 'מקרא', 'מקרא': 'מקרא', 'מקראי': 'מקרא',
  'עברית מקראית': 'מקרא', 'תנ"ך': 'מקרא',
  // Talmudic / Rabbinic
  'חז"ל': 'חז"ל', 'לשון חז"ל': 'חז"ל', 'חזל': 'חז"ל', 'ח': 'חז"ל',
  'תלמודי': 'חז"ל', 'עברית תלמודית': 'חז"ל', 'עברית רבנית': 'חז"ל',
  'רבנית': 'חז"ל', 'הלכה': 'חז"ל',
  // Medieval
  'הביניים': 'ביניים', 'לשון ימי הביניים': 'ביניים', 'ימי הביניים': 'ביניים',
  'עברית ימי הביניים': 'ביניים', 'ב': 'ביניים',
  // Modern (new Hebrew — still useful to tag for potential filtering)
  'עברית חדשה': 'חדשה', 'חדשה': 'חדשה', 'מ': 'חדשה',
}

function derivePeriodTag(definitions) {
  // Return the most "restrictive" period found across all definitions.
  // Priority: מקרא > חז"ל > ביניים > חדשה > null
  const priority = { 'מקרא': 4, 'חז"ל': 3, 'ביניים': 2, 'חדשה': 1 }
  let best = null
  for (const def of definitions) {
    if (!def.layer) continue
    const period = PERIOD_MAP[def.layer.trim()]
    if (period && (!best || priority[period] > priority[best])) best = period
  }
  return best
}

// ── DB setup ──────────────────────────────────────────────────────────────────

function openDb() {
  const db = new Database(DST_DB)
  db.pragma('journal_mode = DELETE')
  db.pragma('synchronous = NORMAL')
  db.pragma('cache_size = -32000')
  db.exec(`CREATE TABLE IF NOT EXISTS _meta (key TEXT PRIMARY KEY, value TEXT)`)
  db.prepare("INSERT OR IGNORE INTO source (label, lang, url) VALUES ('ויקימילון', 'עברית', 'https://he.wiktionary.org')").run()
  const sourceId = db.prepare("SELECT id FROM source WHERE label = 'ויקימילון'").get().id

  const stmts = {
    sourceId,
    insertSense: db.prepare(`INSERT OR IGNORE INTO sense (headword, nikud, pos, binyan, shoresh, ktiv_male, etymology, period_tag, source_id, sense_order) VALUES (?,?,?,?,?,?,?,?,?,?)`),
    getSenseId:  db.prepare(`SELECT id FROM sense WHERE headword = ? AND sense_order = ? LIMIT 1`),
    insertDef:   db.prepare(`INSERT OR IGNORE INTO definition (sense_id, text, filter_tag, def_order) VALUES (?,?,?,?)`),
    insertEx:    db.prepare(`INSERT OR IGNORE INTO example (definition_id, text, source) VALUES (?,?,?)`),
    getOrInsSec: db.prepare(`INSERT OR IGNORE INTO section (sense_id, name) VALUES (?,?)`),
    getSecId:    db.prepare(`SELECT id FROM section WHERE sense_id = ? AND name = ? LIMIT 1`),
    insertSecIt: db.prepare(`INSERT OR IGNORE INTO section_item (section_id, text, item_order) VALUES (?,?,?)`),
    insertTrans: db.prepare(`INSERT OR IGNORE INTO translation (sense_id, lang, word) VALUES (?,?,?)`),
  }
  return { db, stmts }
}

function insertPages(db, stmts, pages) {
  let n = 0
  try {
    db.transaction(() => {
      for (const { title, wikitext } of pages) {
        if (!containsHebrew(title)) continue
        const senses = parsePage(title, wikitext)
        if (!senses.length) continue
        n++
        senses.forEach((sense, senseOrder) => {
          const periodTag = derivePeriodTag(sense.definitions)
          const r = stmts.insertSense.run(
            sense.headword, sense.nikud, sense.pos, sense.binyan,
            sense.shoresh, sense.ktivMale, null, periodTag,
            stmts.sourceId, senseOrder
          )
          const senseId = r.changes > 0 ? Number(r.lastInsertRowid) : stmts.getSenseId.get(sense.headword, senseOrder)?.id
          if (!senseId) return
          sense.definitions.forEach((def, defOrder) => {
            const dr = stmts.insertDef.run(senseId, def.text, def.layer || null, defOrder)
            const defId = dr.changes > 0 ? Number(dr.lastInsertRowid) : null
            if (defId) def.examples.forEach(ex => { if (ex.text) stmts.insertEx.run(defId, ex.text.slice(0, 500), ex.source || null) })
          })
          for (const [secName, items] of Object.entries(sense.sections)) {
            if (secName === 'תרגום' || !items.length) continue
            stmts.getOrInsSec.run(senseId, secName)
            const secId = stmts.getSecId.get(senseId, secName)?.id
            if (secId) items.forEach((text, i) => stmts.insertSecIt.run(secId, text, i))
          }
          sense.translations.forEach(t => t.words.forEach(w => stmts.insertTrans.run(senseId, t.lang, w)))
        })
      }
    })()
  } catch(e) {
    console.error('\nTransaction error:', e.message.substring(0, 200))
  }
  return n
}

// ── Main (same logic as the old working script) ───────────────────────────────

async function main() {
  console.log('Recreating schema...')
  try {
    const { execSync } = require('child_process')
    execSync(`node "${path.resolve(__dirname, 'create-wikidictionary-db.cjs')}"`, { stdio: 'inherit' })
    console.log('Schema recreated OK')
  } catch(e) {
    console.error('SCHEMA ERROR:', e.message)
    process.exit(1)
  }

  const { db, stmts } = openDb()
  console.log('DB path:', DST_DB)
  const senseCount = db.prepare(`SELECT COUNT(*) as c FROM sense`).get().c
  console.log(`DB state: ${senseCount} senses (should be 0)`)

  // Sanity check: try inserting one row
  const testR = stmts.insertSense.run('__test__', null, null, null, null, null, null, null, stmts.sourceId, 0)
  const testCount = db.prepare(`SELECT COUNT(*) as c FROM sense`).get().c
  console.log(`Test insert: changes=${testR.changes}, senses after=${testCount}`)
  if (testCount === 0) { console.error('FATAL: inserts not working!'); db.close(); process.exit(1) }
  // Remove test row
  db.prepare(`DELETE FROM sense WHERE headword='__test__'`).run()

  let apfrom = ''
  let totalImported = 0
  let batchNum = 0
  let apiAvailable = true

  const ALEPH_BET = ['א','ב','ג','ד','ה','ו','ז','ח','ט','י','כ','ל','מ','נ','ס','ע','פ','צ','ק','ר','ש','ת']
  let startLetterIdx = 0
  if (apfrom) {
    const idx = ALEPH_BET.indexOf(apfrom[0])
    if (idx >= 0) startLetterIdx = idx
  }

  console.log(`Starting${apfrom ? ` (resuming from "${apfrom}")` : ''}...`)

  while (true) {
    let titles = []
    let nextApfrom = ''

    // Try allpages API first
    if (apiAvailable) {
      await sleep(DELAY)
      const result = await getTitles(apfrom, 500)
      if (result && result.titles.length > 0) {
        titles = result.titles.filter(t => containsHebrew(t))
        nextApfrom = result.next
        if (!result.next) apiAvailable = false
      } else {
        console.log('\nAPI unavailable — switching to alphabet iteration...')
        apiAvailable = false
      }
    }

    // Fallback: iterate letter by letter
    if (!apiAvailable && titles.length === 0) {
      if (startLetterIdx >= ALEPH_BET.length) break
      const letter = ALEPH_BET[startLetterIdx]
      process.stdout.write(`\n  Letter: ${letter}`)
      await sleep(DELAY)
      const url = `${API_URL}?action=query&list=allpages&apnamespace=0&aplimit=500&apfrom=${encodeURIComponent(letter)}&format=json`
      const res = await get(url)
      if (res.status === 200) {
        try {
          const j = JSON.parse(res.body)
          const all = (j.query?.allpages || []).map(p => p.title)
          titles = all.filter(t => containsHebrew(t) && t[0] === letter)
          startLetterIdx++
          nextApfrom = ALEPH_BET[startLetterIdx] || ''
        } catch { startLetterIdx++; continue }
      } else { startLetterIdx++; await sleep(2000); continue }
    }

    if (titles.length === 0) {
      if (apiAvailable) { apfrom = nextApfrom; continue }
      break
    }

    // Export in sub-batches of BATCH via Special:Export
    for (let i = 0; i < titles.length; i += BATCH) {
      const batch = titles.slice(i, i + BATCH)
      await sleep(DELAY)

      let pages
      try {
        pages = await exportPages(batch)
      } catch (e) {
        console.error('\nExport error:', e.message, '— retrying in 3s')
        await sleep(3000)
        try { pages = await exportPages(batch) }
        catch (e2) { console.error('  Retry failed:', e2.message, '— skipping'); continue }
      }

      const n = insertPages(db, stmts, pages)
      totalImported += n
      batchNum++
      process.stdout.write(`\r  Batch ${batchNum}: ${totalImported} entries | last: "${batch[batch.length-1]?.substring(0,12)}"`)
    }

    // Save progress
    apfrom = nextApfrom
    db.prepare(`INSERT OR REPLACE INTO _meta VALUES ('apfrom',?)`).run(apfrom)

    if (!apfrom && !apiAvailable) break
  }

  db.prepare(`INSERT OR REPLACE INTO _meta VALUES ('done','1')`).run()
  db.pragma('optimize')
  db.close()

  console.log(`\n\nDone! Total entries: ${totalImported}`)
}

main().catch(e => { console.error('\nFatal:', e.message); process.exit(1) })
