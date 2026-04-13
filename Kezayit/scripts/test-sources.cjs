'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

const URLS = [
  { name: 'יד מאיר', url: 'https://www.yadmeir.co.il/?CategoryID=342' },
  { name: 'daat.ac.il', url: 'https://www.daat.ac.il/daat/vl/tohen.asp?id=9' },
  { name: 'he.wiktionary API', url: 'https://he.wiktionary.org/w/api.php?action=query&list=allpages&apnamespace=0&aplimit=5&format=json' },
  { name: 'Wikipedia API', url: 'https://en.wikipedia.org/w/api.php?action=query&titles=List_of_Hebrew_abbreviations&prop=revisions&rvprop=content&rvslots=main&format=json' },
]

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  for (const { name, url } of URLS) {
    try {
      const resp = await page.goto(url, { waitUntil: 'networkidle', timeout: 15000 })
      const status = resp?.status()
      const text = await page.evaluate(() => document.body.innerText.slice(0, 200))
      console.log(`\n[${name}] Status: ${status}`)
      console.log(`  URL: ${resp?.url()}`)
      console.log(`  Content: ${text.slice(0, 150).replace(/\n/g, ' ')}`)
    } catch (e) {
      console.log(`\n[${name}] ERROR: ${e.message.slice(0, 80)}`)
    }
  }

  await browser.close()
}
main().catch(console.error)
