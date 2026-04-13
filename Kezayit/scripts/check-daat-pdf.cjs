'use strict'
const { chromium } = require('playwright')
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
const path = require('path')
const fs = require('fs')

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME_PATH, headless: true })
  const context = await browser.newContext()
  const page = await context.newPage()

  // Set up download handling
  const downloadPath = path.resolve(__dirname, '../old')
  await context.route('**/*.pdf', async route => {
    const response = await route.fetch()
    const body = await response.body()
    const dest = path.join(downloadPath, 'rt01_sample.pdf')
    fs.writeFileSync(dest, body)
    console.log(`Saved PDF: ${(body.length/1024).toFixed(1)} KB`)
    
    // Check if it has text content
    const str = body.toString('latin1')
    const hasTextMarkers = str.includes('/Font') && str.includes('BT ')
    const hasHebrew = /[\xD7\xD6][\x90-\xBF]/.test(str)
    console.log('Has text/font markers:', hasTextMarkers)
    console.log('Has Hebrew UTF-8 bytes:', hasHebrew)
    
    // Show a snippet of the raw content
    console.log('\nRaw content sample (chars 100-400):')
    console.log(str.slice(100, 400).replace(/[^\x20-\x7E\n]/g, '.'))
    
    await route.fulfill({ response })
  })

  console.log('Fetching rt01.pdf via browser...')
  await page.goto('https://www.daat.ac.il/daat/vl/rt/rt01.pdf', { timeout: 20000 })
  
  await new Promise(r => setTimeout(r, 3000))
  await browser.close()
}
main().catch(console.error)
