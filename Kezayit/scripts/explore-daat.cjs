'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  await page.goto('https://www.daat.ac.il/daat/vl/tohen.asp?id=9', { waitUntil: 'networkidle', timeout: 20000 })

  const info = await page.evaluate(() => {
    const text = document.body.innerText.slice(0, 1000)
    const links = Array.from(document.querySelectorAll('a[href]'))
      .map(a => ({ text: a.innerText.trim().slice(0, 60), href: a.href }))
      .filter(l => l.text && l.href.includes('daat'))
      .slice(0, 30)
    return { text, links }
  })

  console.log('Page text:\n', info.text)
  console.log('\nLinks:')
  info.links.forEach(l => console.log(`  [${l.text}] -> ${l.href}`))

  await browser.close()
}
main().catch(console.error)
