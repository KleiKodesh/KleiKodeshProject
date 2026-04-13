'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  // Check kaikki.org Hebrew dictionary page
  await page.goto('https://kaikki.org/dictionary/Hebrew/', { waitUntil: 'networkidle', timeout: 20000 })
  const text = await page.evaluate(() => document.body.innerText.slice(0, 1000))
  const url = page.url()
  console.log('URL:', url)
  console.log('Content:\n', text)

  await browser.close()
}
main().catch(console.error)
