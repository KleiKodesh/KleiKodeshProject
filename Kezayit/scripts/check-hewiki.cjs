'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  // Check kaikki hewiktionary page
  await page.goto('https://kaikki.org/hewiktionary/', { waitUntil: 'networkidle', timeout: 20000 })
  const text1 = await page.evaluate(() => document.body.innerText.slice(0, 500))
  console.log('kaikki hewiktionary:\n', text1)

  // Check he.wiktionary API - how many pages total?
  await page.goto('https://he.wiktionary.org/w/api.php?action=query&meta=siteinfo&siprop=statistics&format=json', { waitUntil: 'networkidle', timeout: 15000 })
  const stats = await page.evaluate(() => document.body.innerText)
  console.log('\nhe.wiktionary stats:\n', stats.slice(0, 300))

  // Check what a sample entry looks like via API
  await page.goto('https://he.wiktionary.org/w/api.php?action=query&titles=שלום&prop=revisions&rvprop=content&rvslots=main&format=json', { waitUntil: 'networkidle', timeout: 15000 })
  const sample = await page.evaluate(() => document.body.innerText)
  console.log('\nSample entry (שלום):\n', sample.slice(0, 500))

  await browser.close()
}
main().catch(console.error)
