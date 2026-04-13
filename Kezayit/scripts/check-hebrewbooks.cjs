'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const page = await browser.newPage()

  // Search HebrewBooks for the Stern abbreviations book
  await page.goto('https://hebrewbooks.org/search?sdesc=ראשי+תיבות+שטערן&sort=0', { waitUntil: 'networkidle', timeout: 20000 })
  const text = await page.evaluate(() => document.body.innerText.slice(0, 1000))
  console.log('Search results:\n', text)

  // Also try direct search
  await page.goto('https://hebrewbooks.org/search?sdesc=%D7%A8%D7%90%D7%A9%D7%99+%D7%AA%D7%99%D7%91%D7%95%D7%AA&sort=0', { waitUntil: 'networkidle', timeout: 20000 })
  const text2 = await page.evaluate(() => document.body.innerText.slice(0, 800))
  console.log('\nHebrewBooks search:\n', text2)

  await browser.close()
}
main().catch(console.error)
