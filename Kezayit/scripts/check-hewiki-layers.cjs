'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

// Check how many entries have the approved layer tags
// and what a few samples look like

async function fetchJson(page, url) {
  await page.goto(url, { waitUntil: 'networkidle', timeout: 20000 })
  return JSON.parse(await page.evaluate(() => document.body.innerText))
}

const API = 'https://he.wiktionary.org/w/api.php'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  // Count entries in each approved category
  const categories = [
    'ספרות',        // literary
    'עברית_מקראית', // biblical Hebrew
    'עברית_תלמודית', // Talmudic Hebrew
    'עברית_רבנית',  // Rabbinic Hebrew
    'ארמית',        // Aramaic
  ]

  console.log('── Category sizes ──')
  for (const cat of categories) {
    const url = `${API}?action=query&list=categorymembers&cmtitle=קטגוריה:${encodeURIComponent(cat)}&cmlimit=1&format=json`
    try {
      const data = await fetchJson(page, url)
      // Get total via separate call
      const url2 = `${API}?action=query&prop=categoryinfo&titles=קטגוריה:${encodeURIComponent(cat)}&format=json`
      const data2 = await fetchJson(page, url2)
      const pages = Object.values(data2?.query?.pages ?? {})[0]
      const count = pages?.categoryinfo?.pages ?? '?'
      console.log(`  ${cat}: ${count} entries`)
    } catch(e) {
      console.log(`  ${cat}: ERROR ${e.message.slice(0,50)}`)
    }
  }

  // Sample a few entries that use the layer templates
  console.log('\n── Sample entries with layer tags ──')
  const searchUrl = `${API}?action=query&list=search&srsearch=hastemplate:רובד&srlimit=5&format=json`
  const searchData = await fetchJson(page, searchUrl)
  const titles = searchData?.query?.search?.map(r => r.title) ?? []
  console.log('Titles with {{רובד}}:', titles)

  // Fetch one to see the wikitext
  if (titles[0]) {
    const wtUrl = `${API}?action=query&titles=${encodeURIComponent(titles[0])}&prop=revisions&rvprop=content&rvslots=main&format=json`
    const wtData = await fetchJson(page, wtUrl)
    const wikitext = Object.values(wtData?.query?.pages ?? {})[0]?.revisions?.[0]?.slots?.main?.['*'] ?? ''
    console.log(`\nWikitext for "${titles[0]}" (first 600 chars):`)
    console.log(wikitext.slice(0, 600))
  }

  await browser.close()
}
main().catch(console.error)
