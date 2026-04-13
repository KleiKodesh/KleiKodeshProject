'use strict'
const https = require('https')

function fetchUrl(url) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, {
      headers: { 'User-Agent': 'Mozilla/5.0', 'Accept': 'application/json' }
    }, (res) => {
      console.log('Status:', res.statusCode)
      console.log('Headers:', JSON.stringify(res.headers).slice(0, 200))
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        console.log('Redirect to:', res.headers.location)
        return fetchUrl(res.headers.location).then(resolve).catch(reject)
      }
      const chunks = []
      res.on('data', c => chunks.push(c))
      res.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')))
      res.on('error', reject)
    })
    req.on('error', reject)
    req.end()
  })
}

async function main() {
  const url = 'https://en.wikipedia.org/w/api.php?action=query&titles=List_of_Hebrew_abbreviations&prop=revisions&rvprop=content&rvslots=main&format=json'
  const raw = await fetchUrl(url)
  console.log('Response length:', raw.length)
  console.log('First 500:', raw.slice(0, 500))
  const json = JSON.parse(raw)
  const pages = json?.query?.pages ?? {}
  const page = Object.values(pages)[0]
  const wikitext = page?.revisions?.[0]?.slots?.main?.['*'] ?? ''
  console.log('Wikitext length:', wikitext.length)
  console.log('First 300:', wikitext.slice(0, 300))
}
main().catch(console.error)
