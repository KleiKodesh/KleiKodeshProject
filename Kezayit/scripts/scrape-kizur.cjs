'use strict'
/**
 * scrape-kizur.cjs
 *
 * Scrapes all abbreviation entries from kizur.co.il using system Chrome.
 * Iterates through all category pages and extracts abbrev → expansion pairs.
 * Imports into public/dictionary.db as source 'קיצור'.
 *
 * Usage: node scripts/scrape-kizur.cjs
 */

const { chromium } = require('playwright')
const Database = require('better-sqlite3')
const path = require('path')

const DST_DB = path.resolve(__dirname, '../public/dictionary.db')
const SOURCE_LABEL = 'קיצור'
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
const BASE = 'http://www.kizur.co.il'

// ── Scrape a single group page ────────────────────────────────────────────────

async function scrapeGroup(page, groupId, groupName) {
  const entries = []
  let pageNum = 1

  while (true) {
    const url = `${BASE}/search_group.php?group=${groupId}&sub_group=0&page=${pageNum}`
    await page.goto(url, { waitUntil: 'networkidle', timeout: 20000 })

    const rows = await page.evaluate(() => {
      const results = []
      document.querySelectorAll('table tr').forEach(tr => {
        const cells = tr.querySelectorAll('td')
        if (cells.length >= 2) {
          const abbrev = cells[0]?.innerText?.trim()
          const expansion = cells[1]?.innerText?.trim()
          if (abbrev && expansion &&
              abbrev.length <= 20 &&
              expansion.length >= 2 && expansion.length <= 200 &&
              /[\u05D0-\u05EA]/.test(abbrev) &&
              /[\u05D0-\u05EA]/.test(expansion)) {
            results.push({ abbrev, expansion })
          }
        }
      })
      return results
    })

    entries.push(...rows)
    process.stdout.write(`  p${pageNum}(${entries.length})`)

    // Pagination text is like "20-1 מתוך 1731" or "1-20 of 1731"
    const pageInfo = await page.evaluate(() => {
      const text = document.body.innerText
      // Hebrew format: "40-21 מתוך 1731"
      const m = text.match(/(\d+)-(\d+)\s+מתוך\s+(\d+)/)
      if (m) {
        const a = parseInt(m[1]), b = parseInt(m[2])
        return { to: Math.max(a, b), total: parseInt(m[3]) }
      }
      return null
    })

    if (!pageInfo || pageInfo.to >= pageInfo.total) break
    pageNum++
    if (pageNum > 500) break
  }

  return entries
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main() {
  console.log('Launching Chrome...')
  const browser = await chromium.launch({
    executablePath: CHROME_PATH,
    headless: true,
  })

  const page = await browser.newPage()

  // Only scrape the Judaism category (group=1)
  const groups = [{ id: 1, name: 'יהדות' }]

  // Scrape all groups
  const allEntries = []
  for (const group of groups) {
    process.stdout.write(`Scraping [${group.name}]... `)
    try {
      const entries = await scrapeGroup(page, group.id, group.name)
      console.log(`${entries.length} entries`)
      allEntries.push(...entries.map(e => ({ ...e, group: group.name })))
    } catch (e) {
      console.log(`ERROR: ${e.message}`)
    }
  }

  await browser.close()

  console.log(`\nTotal scraped: ${allEntries.length}`)
  console.log('\nSample:')
  allEntries.slice(0, 10).forEach(e => console.log(`  [${e.abbrev}] → ${e.expansion} (${e.group})`))

  if (allEntries.length === 0) return

  // ── Import into DB ──────────────────────────────────────────────────────────
  const dst = new Database(DST_DB)
  dst.pragma('journal_mode = WAL')
  dst.pragma('foreign_keys = ON')

  dst.prepare('INSERT OR IGNORE INTO source (label) VALUES (?)').run(SOURCE_LABEL)
  const sourceId = dst.prepare('SELECT id FROM source WHERE label = ?').get(SOURCE_LABEL).id

  // Build existing pairs set
  const existingPairs = new Set(
    dst.prepare(`
      SELECT s.headword, d.text
      FROM sense s JOIN definition d ON d.sense_id = s.id
      WHERE s.source_id != ?
    `).all(sourceId).map(r => `${r.headword}|${r.text}`)
  )

  const insertSense = dst.prepare(`
    INSERT OR IGNORE INTO sense (headword, nikud, etymology, cross_ref, source_id, sense_order)
    VALUES (?, NULL, NULL, NULL, ?, ?)
  `)
  const insertDef = dst.prepare(`
    INSERT OR IGNORE INTO definition (sense_id, text, def_order) VALUES (?, ?, 0)
  `)
  const getSenseId = dst.prepare(`
    SELECT id FROM sense WHERE headword = ? AND source_id = ? AND sense_order = ? LIMIT 1
  `)
  const getMaxOrder = dst.prepare(`
    SELECT COALESCE(MAX(sense_order), -1) as m FROM sense WHERE headword = ? AND source_id = ?
  `)

  let inserted = 0, skipped = 0

  // Normalize: strip nikud, clean whitespace
  function norm(s) {
    return s.replace(/[\u05B0-\u05C7]/g, '').replace(/\s+/g, ' ').trim()
  }

  dst.transaction(() => {
    for (const { abbrev, expansion } of allEntries) {
      const hw = norm(abbrev)
      const def = norm(expansion)
      if (!hw || !def || hw.length > 30) continue

      if (existingPairs.has(`${hw}|${def}`)) { skipped++; continue }

      const nextOrder = getMaxOrder.get(hw, sourceId).m + 1
      const r = insertSense.run(hw, sourceId, nextOrder)
      const senseId = r.changes > 0
        ? Number(r.lastInsertRowid)
        : getSenseId.get(hw, sourceId, nextOrder)?.id
      if (senseId) {
        insertDef.run(senseId, def)
        inserted++
        existingPairs.add(`${hw}|${def}`)
      }
    }
  })()

  const total = dst.prepare('SELECT COUNT(*) as c FROM sense').get().c
  const sources = dst.prepare('SELECT label, COUNT(*) as c FROM sense s JOIN source src ON src.id=s.source_id GROUP BY src.id ORDER BY src.id').all()
  dst.close()

  console.log(`\nInserted: ${inserted}, Skipped: ${skipped}`)
  console.log(`Total senses: ${total}`)
  console.log('\nSources:')
  sources.forEach(s => console.log(`  ${s.label}: ${s.c}`))
}

main().catch(console.error)
