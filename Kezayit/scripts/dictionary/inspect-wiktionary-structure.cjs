/**
 * Fetches a few real Hebrew Wiktionary pages and prints their raw wikitext
 * so we can understand the full structure before designing the schema.
 */
const https = require('https')

const WORDS = ['ברא', 'שלום', 'ספר', 'כתב', 'אהב']
const API = 'https://he.wiktionary.org/w/api.php'

function get(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { 'User-Agent': 'KezayitDictBot/1.0' } }, res => {
      let data = ''
      res.on('data', c => data += c)
      res.on('end', () => { try { resolve(JSON.parse(data)) } catch(e) { reject(e) } })
    }).on('error', reject)
  })
}

async function main() {
  for (const word of WORDS) {
    const url = `${API}?action=query&titles=${encodeURIComponent(word)}&prop=revisions&rvprop=content&rvslots=main&format=json`
    const data = await get(url)
    const pages = data?.query?.pages || {}
    const page = Object.values(pages)[0]
    const wikitext = page?.revisions?.[0]?.slots?.main?.['*'] || page?.revisions?.[0]?.['*'] || ''
    console.log(`\n${'='.repeat(60)}`)
    console.log(`WORD: ${word}`)
    console.log('='.repeat(60))
    console.log(wikitext.substring(0, 3000))
    console.log('...(truncated)')
    await new Promise(r => setTimeout(r, 300))
  }
}

main().catch(console.error)
