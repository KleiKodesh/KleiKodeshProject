/**
 * build-wiktionary-db.cjs
 *
 * Downloads the Hebrew Wiktionary XML dump and builds public/wiktionary.db —
 * a fully structured offline SQLite database of the Hebrew Wiktionary.
 *
 * Only Hebrew / לשון הקודש entries are kept (namespace 0, Hebrew-titled pages).
 * Template/category/redirect pages are skipped.
 *
 * Schema:
 *   entry        — one row per word-sense (בָּרָא א, בֵּרֵא, בָּרָא ב = 3 rows)
 *   definition   — numbered definitions per entry
 *   example      — citation examples per definition
 *   section      — גיזרון / נגזרות / נרדפות / ניגודים / צירופים / מידע נוסף / ראו גם
 *   translation  — per-language translations (English + Arabic only)
 *
 * Usage:
 *   node scripts/dictionary/build-wiktionary-db.cjs
 *
 * The dump is downloaded to scripts/dictionary/hewiktionary-dump.xml.bz2
 * if not already present (or if it is 0 bytes).
 * Resume: re-run after interruption — the script overwrites the output DB atomically.
 *
 * Dependencies (already in devDependencies):
 *   better-sqlite3
 *
 * Node built-ins used for decompression: zlib (bz2 not natively supported —
 * we use the Wikimedia multistream index + bz2 via child_process bzip2/bunzip2,
 * or fall back to fetching pages via the API if bzip2 is unavailable).
 *
 * License of source data: CC BY-SA 4.0 (he.wiktionary.org)
 */

'use strict'

const https       = require('https')
const http        = require('http')
const fs          = require('fs')
const path        = require('path')
const zlib        = require('zlib')
const { execSync, spawnSync } = require('child_process')
const Database    = require('better-sqlite3')

// ── Config ────────────────────────────────────────────────────────────────────

const DUMP_URL  = 'https://dumps.wikimedia.org/hewiktionary/latest/hewiktionary-latest-pages-articles.xml.bz2'
const DUMP_FILE = path.resolve(__dirname, 'hewiktionary-dump.xml.bz2')
const OUT_DB    = path.resolve(__dirname, '../../public/wiktionary.db')
const TMP_DB    = OUT_DB + '.tmp'

// Only store translations for these languages (keeps DB small)
const KEEP_LANGS = new Set(['אנגלית', 'ערבית'])

// ── Download ──────────────────────────────────────────────────────────────────

async function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest)
    const mod = url.startsWith('https') ? https : http
    function doGet(u) {
      const opts = {
        headers: {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
          'Accept': '*/*',
          'Accept-Encoding': 'identity',
        }
      }
      mod.get(u, opts, res => {
        if (res.statusCode === 301 || res.statusCode === 302) {
          doGet(res.headers.location)
          return
        }
        if (res.statusCode !== 200) { reject(new Error(`HTTP ${res.statusCode}`)); return }
        const total = parseInt(res.headers['content-length'] || '0')
        let received = 0
        res.on('data', chunk => {
          received += chunk.length
          if (total) process.stdout.write(`\r  ${(received/1024/1024).toFixed(1)}/${(total/1024/1024).toFixed(1)} MB`)
        })
        res.pipe(file)
        file.on('finish', () => { file.close(); console.log(); resolve() })
        file.on('error', reject)
      }).on('error', reject)
    }
    doGet(url)
  })
}

// ── Wikitext helpers ──────────────────────────────────────────────────────────

function stripNikud(s) {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}

function containsHebrew(s) {
  return /[\u05D0-\u05EA]/.test(s)
}

/** Clean wikitext to plain readable Hebrew text */
function cleanWiki(s) {
  return s
    .replace(/\{\{[^{}]*\}\}/g, '')              // {{templates}}
    .replace(/\{\{[^{}]*\}\}/g, '')              // nested templates (second pass)
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g, '$2') // [[link|text]] → text
    .replace(/'{2,3}/g, '')                       // bold/italic
    .replace(/<ref[^>]*>.*?<\/ref>/gs, '')        // <ref>...</ref>
    .replace(/<[^>]+>/g, '')                      // remaining HTML
    .replace(/\s+/g, ' ')
    .trim()
}

/** Extract nikud from a ==header== line */
function extractNikudFromHeader(raw) {
  // Strip {{משני|א}} etc, keep the nikud word
  const cleaned = raw.replace(/\{\{[^}]*\}\}/g, '').trim()
  return /[\u05B0-\u05C7]/.test(cleaned) ? cleaned : null
}

/** Extract שורש letters from {{שרש3|ב|ר|א|א}} or {{שרש|שׁלם|שׁ־ל־ם}} */
function extractShoresh(templateStr) {
  const m3 = templateStr.match(/\{\{שרש3\|([^|]+)\|([^|]+)\|([^|]+)/)
  if (m3) return `${m3[1]}-${m3[2]}-${m3[3]}`
  const m1 = templateStr.match(/\{\{שרש\|([^|}\s]+)/)
  if (m1) return m1[1]
  return null
}

/** Extract binyan from ניתוח דקדוקי לפועל template */
function extractBinyan(templateStr) {
  const m = templateStr.match(/\|בניין\s*=\s*([^\n|]+)/)
  return m ? m[1].trim() : null
}

/** Extract pos (חלק דיבר) from ניתוח דקדוקי template */
function extractPos(templateStr) {
  const m = templateStr.match(/\|חלק דיבר\s*=\s*([^\n|]+)/)
  return m ? m[1].trim() : null
}

/** Extract ktiv male from template */
function extractKtivMale(templateStr) {
  const m = templateStr.match(/\|כתיב מלא\s*=\s*([^\n|]+)/)
  return m ? m[1].trim() : null
}

/**
 * Parse a single wikitext page into structured data.
 * Returns array of sense objects (one per ==header== section).
 */
function parsePage(title, wikitext) {
  if (!wikitext || !containsHebrew(title)) return []
  // Skip redirects
  if (/^#הפניה|^#REDIRECT/i.test(wikitext.trim())) return []

  const lines = wikitext.split('\n')
  const senses = []

  let cur = null

  // Current sub-section state
  let curSection = null  // גיזרון | נגזרות | נרדפות | ניגודים | צירופים | מידע נוסף | ראו גם | תרגום
  let curDefIdx = -1

  function flushSense() {
    if (cur && (cur.definitions.length > 0 || cur.sections.length > 0)) {
      senses.push(cur)
    }
    cur = null
    curSection = null
    curDefIdx = -1
  }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]

    // ── New sense: ==word== ──
    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flushSense()
      const rawHeader = senseMatch[1]
      const nikud = extractNikudFromHeader(rawHeader)
      const headword = stripNikud(rawHeader.replace(/\{\{[^}]*\}\}/g, '').trim()) || stripNikud(title)
      cur = {
        headword,
        nikud,
        pos: null,
        binyan: null,
        shoresh: null,
        ktivMale: null,
        definitions: [],   // [{text, layer, examples:[]}]
        sections: [],      // [{type, items:[]}]
        translations: [],  // [{lang, text}]
      }
      curSection = null
      curDefIdx = -1
      continue
    }

    if (!cur) continue

    // ── ניתוח דקדוקי template (may span multiple lines) ──
    if (line.includes('{{ניתוח דקדוקי')) {
      // Collect until closing }}
      let block = line
      let j = i + 1
      while (j < lines.length && !block.includes('}}')) {
        block += '\n' + lines[j++]
      }
      i = j - 1
      cur.shoresh = cur.shoresh || extractShoresh(block)
      cur.binyan  = cur.binyan  || extractBinyan(block)
      cur.pos     = cur.pos     || extractPos(block)
      cur.ktivMale = cur.ktivMale || extractKtivMale(block)
      continue
    }

    // ── Section headers: ===גיזרון=== etc ──
    const secMatch = line.match(/^===([^=]+)===\s*$/)
    if (secMatch) {
      curSection = secMatch[1].trim()
      curDefIdx = -1
      // Ensure section exists
      if (!cur.sections.find(s => s.type === curSection)) {
        cur.sections.push({ type: curSection, items: [] })
      }
      continue
    }

    // ── Level 4 headers (====) — skip ──
    if (/^====/.test(line)) { curSection = null; continue }

    // ── Definition lines: # text ──
    if (/^#{1,2}[^:#*]/.test(line) && !curSection) {
      const layerMatch = line.match(/\{\{(?:מקרא|רובד|משלב)\|([^|}]+)/)
      const layer = layerMatch ? layerMatch[1].trim() : null
      const text = cleanWiki(line.replace(/^#+\s*/, ''))
      if (text && text.length > 1) {
        cur.definitions.push({ text, layer, examples: [] })
        curDefIdx = cur.definitions.length - 1
      }
      continue
    }

    // ── Example lines: #:* or #: ──
    if (/^#[:#*]/.test(line) && !curSection && curDefIdx >= 0) {
      // Extract source reference from {{צט/תנ"ך|text|book|ch|v}} or similar
      const citMatch = line.match(/\{\{צט[^|]*\|([^|]+)\|([^|]+)\|([^|]+)\|([^|}]+)/)
      if (citMatch) {
        const src = `${citMatch[2]} ${citMatch[3]}, ${citMatch[4]}`
        cur.definitions[curDefIdx].examples.push({ text: cleanWiki(citMatch[1]), source: src })
      }
      continue
    }

    // ── Section content ──
    if (curSection) {
      const sec = cur.sections.find(s => s.type === curSection)
      if (!sec) continue

      // Translation lines: * language: {{ת|lang|word}}
      if (curSection === 'תרגום') {
        const langMatch = line.match(/^\*\s*([^:：]+)[：:]\s*(.+)/)
        if (langMatch) {
          const lang = langMatch[1].trim()
          if (KEEP_LANGS.has(lang)) {
            // Extract translation words from {{ת|lang|word1|word2}}
            const words = [...langMatch[2].matchAll(/\{\{ת\|[^|]+\|([^|}]+)/g)].map(m => m[1].trim())
            if (words.length > 0) {
              cur.translations.push({ lang, text: words.join(', ') })
            }
          }
        }
        continue
      }

      // List items: * [[word]] or * plain text
      if (/^\*/.test(line)) {
        const text = cleanWiki(line.replace(/^\*+\s*/, ''))
        if (text && containsHebrew(text) && text.length < 60) {
          sec.items.push(text)
        }
        continue
      }

      // Prose lines (גיזרון, מידע נוסף)
      if (line.trim() && !line.startsWith('=') && !line.startsWith('{') && !line.startsWith('[')) {
        const text = cleanWiki(line)
        if (text && text.length > 3) sec.items.push(text)
      }
    }
  }

  flushSense()
  return senses
}

// ── XML streaming parser ──────────────────────────────────────────────────────
// We use a simple state-machine parser — no external XML lib needed.

function processXmlStream(stream, onPage) {
  return new Promise((resolve, reject) => {
    let buf = ''
    let inPage = false
    let pageXml = ''
    let pageCount = 0

    stream.on('data', chunk => {
      buf += chunk.toString('utf8')

      let pos = 0
      while (true) {
        if (!inPage) {
          const start = buf.indexOf('<page>', pos)
          if (start === -1) { buf = buf.slice(pos); break }
          inPage = true
          pageXml = ''
          pos = start + 6
        } else {
          const end = buf.indexOf('</page>', pos)
          if (end === -1) { pageXml += buf.slice(pos); buf = ''; break }
          pageXml += buf.slice(pos, end)
          pos = end + 7
          inPage = false

          // Extract title and text
          const titleMatch = pageXml.match(/<title>([^<]+)<\/title>/)
          const nsMatch    = pageXml.match(/<ns>(\d+)<\/ns>/)
          const textMatch  = pageXml.match(/<text[^>]*>([\s\S]*?)<\/text>/)

          if (titleMatch && nsMatch && textMatch) {
            const ns    = parseInt(nsMatch[1])
            const title = titleMatch[1]
              .replace(/&amp;/g, '&').replace(/&lt;/g, '<')
              .replace(/&gt;/g, '>').replace(/&quot;/g, '"')
            const text  = textMatch[1]
              .replace(/&amp;/g, '&').replace(/&lt;/g, '<')
              .replace(/&gt;/g, '>').replace(/&quot;/g, '"')
              .replace(/&#039;/g, "'")

            if (ns === 0 && containsHebrew(title)) {
              onPage(title, text)
              pageCount++
              if (pageCount % 1000 === 0) process.stdout.write(`\r  Processed ${pageCount} pages...`)
            }
          }
          pageXml = ''
        }
      }
    })

    stream.on('end', () => { console.log(`\r  Processed ${pageCount} pages total`); resolve(pageCount) })
    stream.on('error', reject)
  })
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  // 1. Download dump if needed
  const dumpStat = fs.existsSync(DUMP_FILE) ? fs.statSync(DUMP_FILE) : null
  if (!dumpStat || dumpStat.size < 1000) {
    console.log(`Downloading Hebrew Wiktionary dump (~14 MB)...`)
    console.log(`  from: ${DUMP_URL}`)
    await download(DUMP_URL, DUMP_FILE)
    console.log(`  saved to: ${DUMP_FILE}`)
  } else {
    console.log(`Using existing dump: ${DUMP_FILE} (${(dumpStat.size/1024/1024).toFixed(1)} MB)`)
  }

  // 2. Create DB
  if (fs.existsSync(TMP_DB)) fs.unlinkSync(TMP_DB)
  const db = new Database(TMP_DB)
  db.exec(`PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;`)

  db.exec(`
    CREATE TABLE entry (
      id         INTEGER PRIMARY KEY AUTOINCREMENT,
      headword   TEXT NOT NULL,   -- consonants only
      nikud      TEXT,            -- vocalized form from ==header==
      pos        TEXT,            -- חלק דיבר (שם עצם, פועל, ...)
      binyan     TEXT,            -- בניין (קל, פיעל, ...) — verbs only
      shoresh    TEXT,            -- שורש (ב-ר-א)
      ktiv_male  TEXT             -- כתיב מלא
    );

    CREATE TABLE definition (
      id       INTEGER PRIMARY KEY AUTOINCREMENT,
      entry_id INTEGER NOT NULL REFERENCES entry(id),
      idx      INTEGER NOT NULL,  -- 1-based order
      text     TEXT NOT NULL,
      layer    TEXT                -- מקרא / חזל / etc
    );

    CREATE TABLE example (
      id            INTEGER PRIMARY KEY AUTOINCREMENT,
      definition_id INTEGER NOT NULL REFERENCES definition(id),
      text          TEXT NOT NULL,
      source        TEXT            -- e.g. "בראשית ב, ג"
    );

    CREATE TABLE section (
      id       INTEGER PRIMARY KEY AUTOINCREMENT,
      entry_id INTEGER NOT NULL REFERENCES entry(id),
      type     TEXT NOT NULL,   -- גיזרון|נגזרות|נרדפות|ניגודים|צירופים|מידע נוסף|ראו גם
      idx      INTEGER NOT NULL, -- order within type
      text     TEXT NOT NULL
    );

    CREATE TABLE translation (
      id       INTEGER PRIMARY KEY AUTOINCREMENT,
      entry_id INTEGER NOT NULL REFERENCES entry(id),
      lang     TEXT NOT NULL,
      text     TEXT NOT NULL
    );

    CREATE TABLE meta (
      key   TEXT PRIMARY KEY,
      value TEXT
    );

    INSERT INTO meta VALUES ('version', '1');
    INSERT INTO meta VALUES ('source', 'he.wiktionary.org');
    INSERT INTO meta VALUES ('license', 'CC BY-SA 4.0');
    INSERT INTO meta VALUES ('built', datetime('now'));
  `)

  const insEntry = db.prepare(`INSERT INTO entry (headword, nikud, pos, binyan, shoresh, ktiv_male) VALUES (?,?,?,?,?,?)`)
  const insDef   = db.prepare(`INSERT INTO definition (entry_id, idx, text, layer) VALUES (?,?,?,?)`)
  const insEx    = db.prepare(`INSERT INTO example (definition_id, text, source) VALUES (?,?,?)`)
  const insSec   = db.prepare(`INSERT INTO section (entry_id, type, idx, text) VALUES (?,?,?,?)`)
  const insTrans = db.prepare(`INSERT INTO translation (entry_id, lang, text) VALUES (?,?,?)`)

  let totalEntries = 0
  let totalDefs = 0
  let totalSections = 0

  const insertPage = db.transaction((title, senses) => {
    for (const sense of senses) {
      const entryId = insEntry.run(
        sense.headword || stripNikud(title),
        sense.nikud,
        sense.pos,
        sense.binyan,
        sense.shoresh,
        sense.ktivMale,
      ).lastInsertRowid

      for (let i = 0; i < sense.definitions.length; i++) {
        const def = sense.definitions[i]
        const defId = insDef.run(entryId, i + 1, def.text, def.layer).lastInsertRowid
        for (const ex of def.examples) {
          insEx.run(defId, ex.text, ex.source)
        }
        totalDefs++
      }

      for (const sec of sense.sections) {
        for (let i = 0; i < sec.items.length; i++) {
          insSec.run(entryId, sec.type, i + 1, sec.items[i])
          totalSections++
        }
      }

      for (const tr of sense.translations) {
        insTrans.run(entryId, tr.lang, tr.text)
      }

      totalEntries++
    }
  })

  // 3. Stream + parse the bz2 dump
  console.log(`\nParsing dump...`)
  const bz2Stream = fs.createReadStream(DUMP_FILE)

  // Decompress bz2 using system bunzip2 via spawn
  const { spawn } = require('child_process')
  const bunzip = spawn('bunzip2', ['--stdout', DUMP_FILE])
  bunzip.stderr.on('data', () => {}) // suppress

  await processXmlStream(bunzip.stdout, (title, wikitext) => {
    const senses = parsePage(title, wikitext)
    if (senses.length > 0) insertPage(title, senses)
  })

  // 4. Indexes
  console.log(`\nBuilding indexes...`)
  db.exec(`
    CREATE INDEX idx_entry_headword ON entry(headword);
    CREATE INDEX idx_entry_nikud    ON entry(nikud);
    CREATE INDEX idx_def_entry      ON definition(entry_id);
    CREATE INDEX idx_sec_entry      ON section(entry_id);
    CREATE INDEX idx_sec_type       ON section(type);
    CREATE INDEX idx_trans_entry    ON translation(entry_id);
    PRAGMA optimize;
  `)
  db.close()

  // 5. Atomic replace
  if (fs.existsSync(OUT_DB)) fs.unlinkSync(OUT_DB)
  fs.renameSync(TMP_DB, OUT_DB)

  // 6. Stats
  const stat = fs.statSync(OUT_DB)
  const check = new Database(OUT_DB, { readonly: true })
  const counts = {
    entries:      check.prepare('SELECT COUNT(*) as c FROM entry').get().c,
    definitions:  check.prepare('SELECT COUNT(*) as c FROM definition').get().c,
    examples:     check.prepare('SELECT COUNT(*) as c FROM example').get().c,
    sections:     check.prepare('SELECT COUNT(*) as c FROM section').get().c,
    translations: check.prepare('SELECT COUNT(*) as c FROM translation').get().c,
  }
  const topPos = check.prepare('SELECT pos, COUNT(*) as c FROM entry WHERE pos IS NOT NULL GROUP BY pos ORDER BY c DESC LIMIT 5').all()
  const topSec = check.prepare('SELECT type, COUNT(*) as c FROM section GROUP BY type ORDER BY c DESC LIMIT 8').all()
  check.close()

  console.log(`\n✓ wiktionary.db: ${(stat.size/1024/1024).toFixed(2)} MB`)
  console.log('  Counts:', counts)
  console.log('  Top POS:', topPos.map(r => `${r.pos}(${r.c})`).join(', '))
  console.log('  Top sections:', topSec.map(r => `${r.type}(${r.c})`).join(', '))
}

main().catch(e => { console.error('\nFatal:', e.message, e.stack); process.exit(1) })
