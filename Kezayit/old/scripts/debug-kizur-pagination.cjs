'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
const BASE = 'http://www.kizur.co.il'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  // Check page 1 of army category
  await page.goto(`${BASE}/search_group.php?group=5&page=1`, { waitUntil: 'networkidle', timeout: 20000 })

  const info = await page.evaluate(() => {
    // Find the pagination text
    const text = document.body.innerText
    const m = text.match(/ערכים\s+(\d+)-(\d+)\s+מתוך\s+(\d+)/)

    // Count table rows
    const rows = document.querySelectorAll('table tr')
    const validRows = Array.from(rows).filter(tr => {
      const cells = tr.querySelectorAll('td')
      if (cells.length < 2) return false
      const abbrev = cells[0]?.innerText?.trim()
      return abbrev && abbrev.length <= 20 && /[\u05D0-\u05EA]/.test(abbrev)
    })

    // Find all page links
    const pageLinks = Array.from(document.querySelectorAll('a')).filter(a =>
      a.href.includes('page=')
    ).map(a => ({ text: a.innerText.trim(), href: a.href }))

    return {
      paginationText: m ? `${m[1]}-${m[2]} of ${m[3]}` : 'not found',
      totalRows: rows.length,
      validRows: validRows.length,
      pageLinks: pageLinks.slice(0, 10),
      bodySnippet: text.slice(0, 300)
    }
  })

  console.log('Page info:', JSON.stringify(info, null, 2))

  // Try page 2
  await page.goto(`${BASE}/search_group.php?group=5&page=2`, { waitUntil: 'networkidle', timeout: 20000 })
  const info2 = await page.evaluate(() => {
    const text = document.body.innerText
    const m = text.match(/ערכים\s+(\d+)-(\d+)\s+מתוך\s+(\d+)/)
    return { paginationText: m ? `${m[1]}-${m[2]} of ${m[3]}` : 'not found' }
  })
  console.log('Page 2:', info2)

  await browser.close()
}
main().catch(console.error)
