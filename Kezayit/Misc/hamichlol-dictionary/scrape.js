/**
 * המכלול פירושונים scraper
 *
 * Fetches all ~20,885 disambiguation pages from המכלול and writes each
 * bullet entry into a SQLite DB.
 *
 * Usage:
 *   node scrape.js           — fresh run (overwrites existing DB)
 *   node scrape.js --resume  — resume from checkpoint file
 *
 * Output: hamichlol_disambig.db
 *
 * Schema:
 *   entry(id, page_title, headword, definition, link_target)
 */

'use strict';

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const https = require('https');
const fs = require('fs');
const path = require('path');
const initSqlJs = require('sql.js');

// ── Config ────────────────────────────────────────────────────────────────────

const DB_PATH       = path.join(__dirname, 'hamichlol_disambig.db');
const CKPT_PATH     = path.join(__dirname, 'scrape_checkpoint.json');
const API_BASE      = 'https://he.hamichlol.org.il/w/api.php';
const CATEGORY      = 'קטגוריה:פירושונים';
const PAGE_LIST_LIMIT = 500;   // max per categorymembers call
const WIKITEXT_BATCH  = 50;    // titles per revisions call (API max is 50 for anon)
const POLITE_DELAY_MS = 250;   // delay between API calls
const FLUSH_EVERY     = 2000;  // write DB to disk every N pages processed
const RESUME = process.argv.includes('--resume');

// ── HTTP helper ───────────────────────────────────────────────────────────────

function get(url) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, {
      headers: { 'User-Agent': 'HamichlolScraper/1.0 (offline dictionary builder; contact: local)' },
    }, res => {
      let data = '';
      res.on('data', chunk => (data += chunk));
      res.on('end', () => {
        try { resolve(JSON.parse(data)); }
        catch (e) { reject(new Error('JSON parse error: ' + e.message + '\nBody: ' + data.slice(0, 300))); }
      });
    });
    req.on('error', reject);
    req.setTimeout(30000, () => { req.destroy(); reject(new Error('Request timeout: ' + url.slice(0, 80))); });
  });
}

function delay(ms) { return new Promise(r => setTimeout(r, ms)); }

// ── Wikitext parser ───────────────────────────────────────────────────────────

/**
 * Parses the wikitext of a פירושונים page into structured entries.
 *
 * Handles lines like:
 *   * [[מילה (בלשנות)]] – יחידה בסיסית של השפה
 *   * [[מילה (בלשנות)|מילה]] – יחידה בסיסית של השפה
 *   ** [[ברית מילה]] – מצווה ביהדות...
 *
 * Returns array of { headword, definition, linkTarget }
 */
function parseWikitext(wikitext) {
  const entries = [];
  for (const line of wikitext.split('\n')) {
    // Bullet lines at any depth with a wikilink + dash separator
    const m = line.match(/^\*+\s*\[\[([^\]|]+)(?:\|([^\]]+))?\]\]\s*[–—\-]+\s*(.+)/);
    if (!m) continue;

    const linkTarget  = m[1].trim();
    const displayText = m[2] ? m[2].trim() : linkTarget;
    const definition  = m[3]
      .replace(/\[\[([^\]|]+)\|([^\]]+)\]\]/g, '$2') // [[t|d]] → d
      .replace(/\[\[([^\]]+)\]\]/g, '$1')             // [[t]] → t
      .replace(/'{2,3}/g, '')                         // bold/italic
      .replace(/\{\{[^}]+\}\}/g, '')                  // templates
      .trim();

    if (!displayText || !definition) continue;
    entries.push({ headword: displayText, definition, linkTarget });
  }
  return entries;
}

// ── Checkpoint helpers ────────────────────────────────────────────────────────

function loadCheckpoint() {
  if (!RESUME || !fs.existsSync(CKPT_PATH)) return { cmcontinue: null, pagesDone: 0 };
  try { return JSON.parse(fs.readFileSync(CKPT_PATH, 'utf8')); }
  catch { return { cmcontinue: null, pagesDone: 0 }; }
}

function saveCheckpoint(data) {
  fs.writeFileSync(CKPT_PATH, JSON.stringify(data), 'utf8');
}

// ── DB helpers ────────────────────────────────────────────────────────────────

function flushDb(db) {
  const data = db.export();
  fs.writeFileSync(DB_PATH, Buffer.from(data));
}

// ── Category page iterator ────────────────────────────────────────────────────

async function* iterCategoryPages(startContinue) {
  let cmcontinue = startContinue;
  do {
    let url = `${API_BASE}?action=query&list=categorymembers`
      + `&cmtitle=${encodeURIComponent(CATEGORY)}`
      + `&cmlimit=${PAGE_LIST_LIMIT}&format=json&formatversion=2`;
    if (cmcontinue) url += '&cmcontinue=' + encodeURIComponent(cmcontinue);

    const r = await get(url);
    cmcontinue = r.continue ? r.continue.cmcontinue : null;
    if (cmcontinue === undefined) cmcontinue = null;

    for (const m of r.query.categorymembers) {
      yield { title: m.title, cmcontinue };
    }

    await delay(POLITE_DELAY_MS);
  } while (cmcontinue);
}

// ── Fetch wikitext for a batch of titles ─────────────────────────────────────

async function fetchWikitextBatch(titles) {
  const url = `${API_BASE}?action=query&prop=revisions&rvprop=content&rvslots=main`
    + `&format=json&formatversion=2`
    + `&titles=${titles.map(encodeURIComponent).join('|')}`;
  const r = await get(url);
  const result = {};
  for (const page of r.query.pages) {
    if (page.missing) continue;
    const content = page.revisions?.[0]?.slots?.main?.content;
    if (content) result[page.title] = content;
  }
  return result;
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  console.log(RESUME ? '▶ Resuming scrape...' : '▶ Starting fresh scrape...');
  console.log('  Output:', DB_PATH);

  // Init sql.js
  const SQL = await initSqlJs();

  // Load existing DB if resuming, otherwise start fresh
  let db;
  if (RESUME && fs.existsSync(DB_PATH)) {
    const fileBuffer = fs.readFileSync(DB_PATH);
    db = new SQL.Database(fileBuffer);
    console.log('  Loaded existing DB.');
  } else {
    if (fs.existsSync(DB_PATH)) fs.unlinkSync(DB_PATH);
    db = new SQL.Database();
  }

  db.run(`
    CREATE TABLE IF NOT EXISTS entry (
      headword    TEXT NOT NULL,
      definition  TEXT NOT NULL,
      link_target TEXT NOT NULL
    );
    CREATE INDEX IF NOT EXISTS idx_headword ON entry(headword);
  `);

  const ckpt = loadCheckpoint();
  let { cmcontinue: startContinue, pagesDone } = ckpt;
  console.log(`  Resuming from page ${pagesDone}, cmcontinue: ${startContinue ? startContinue.slice(0, 30) + '...' : 'start'}\n`);

  const insertEntry = (row) => db.run(
    'INSERT INTO entry (headword, definition, link_target) VALUES (?,?,?)',
    [row.headword, row.definition, row.link_target]
  );

  let totalPages   = pagesDone;
  let totalEntries = 0;
  let titleBuffer  = [];
  let lastCmcontinue = startContinue;
  const startTime  = Date.now();

  // Count existing entries if resuming
  if (RESUME) {
    const r = db.exec('SELECT COUNT(*) FROM entry');
    totalEntries = r[0]?.values[0][0] ?? 0;
  }

  async function flushBuffer(currentCmcontinue) {
    if (!titleBuffer.length) return;

    let wikitexts;
    try {
      wikitexts = await fetchWikitextBatch(titleBuffer);
    } catch (e) {
      console.error('\n  Wikitext fetch error:', e.message, '— skipping batch');
      titleBuffer = [];
      return;
    }

    db.run('BEGIN');
    for (const title of titleBuffer) {
      const wt = wikitexts[title];
      if (!wt) continue;
      const entries = parseWikitext(wt);
      for (const e of entries) {
        insertEntry({ headword: e.headword, definition: e.definition, link_target: e.linkTarget });
        totalEntries++;
      }
      totalPages++;
    }
    db.run('COMMIT');

    titleBuffer = [];

    // Checkpoint
    saveCheckpoint({ cmcontinue: currentCmcontinue, pagesDone: totalPages });

    // Flush DB to disk periodically
    if (totalPages % FLUSH_EVERY < WIKITEXT_BATCH) {
      flushDb(db);
    }

    const elapsed = ((Date.now() - startTime) / 1000).toFixed(0);
    const rate    = (totalPages / Math.max(elapsed, 1) * 60).toFixed(0);
    process.stdout.write(
      `\r  Pages: ${totalPages.toLocaleString()} | Entries: ${totalEntries.toLocaleString()} | ${rate} pages/min | ${elapsed}s   `
    );

    await delay(POLITE_DELAY_MS);
  }

  console.log('  Fetching pages...\n');

  for await (const item of iterCategoryPages(startContinue)) {
    lastCmcontinue = item.cmcontinue;
    titleBuffer.push(item.title);

    if (titleBuffer.length >= WIKITEXT_BATCH) {
      await flushBuffer(lastCmcontinue);
    }
  }

  // Final flush
  await flushBuffer(null);

  // Final DB write
  db.run('ANALYZE');
  flushDb(db);
  db.close();

  // Clean up checkpoint
  if (fs.existsSync(CKPT_PATH)) fs.unlinkSync(CKPT_PATH);

  const stat = fs.statSync(DB_PATH);
  const mb   = (stat.size / 1024 / 1024).toFixed(1);

  console.log('\n\n✓ Done!');
  console.log(`  Pages processed : ${totalPages.toLocaleString()}`);
  console.log(`  Total entries   : ${totalEntries.toLocaleString()}`);
  console.log(`  DB file size    : ${mb} MB`);
}

main().catch(err => {
  console.error('\nFatal error:', err instanceof Error ? err.message : err);
  console.error(err);
  process.exit(1);
});
