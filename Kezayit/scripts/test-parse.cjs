'use strict'
// Test parsePage with a real page
const https = require('https')

function post(url, body) {
  return new Promise((resolve, reject) => {
    const u = new URL(url)
    const req = https.request({
      hostname: u.hostname, path: u.pathname, method: 'POST',
      headers: { 'User-Agent': 'KezayitDictBot/1.0', 'Content-Type': 'application/x-www-form-urlencoded', 'Content-Length': Buffer.byteLength(body) }
    }, res => { let d = ''; res.on('data', c => d += c); res.on('end', () => resolve(d)) })
    req.on('error', reject); req.write(body); req.end()
  })
}

// Inline the parsePage from import-wiktionary.cjs
function stripNikud(s) { return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim() }
function containsHebrew(s) { return /[\u05D0-\u05EA]/.test(s) }
function cleanWiki(s) {
  return s.replace(/\{\{[^{}]*\}\}/g,'').replace(/\{\{[^{}]*\}\}/g,'')
    .replace(/\[\[([^\]|]+\|)?([^\]]+)\]\]/g,'$2').replace(/'{2,3}/g,'')
    .replace(/<ref[^>]*>[\s\S]*?<\/ref>/g,'').replace(/<[^>]+>/g,'')
    .replace(/\s+/g,' ').trim()
}
function extractFromTemplate(block, key) { const m = block.match(new RegExp(`\\|${key}\\s*=\\s*([^\\n|]+)`)); return m ? m[1].trim() : null }
function extractShoresh(block) {
  const m3 = block.match(/\{\{שרש3\|([^|]+)\|([^|]+)\|([^|]+)/)
  if (m3) return `${m3[1].trim()}-${m3[2].trim()}-${m3[3].trim()}`
  const m1 = block.match(/\{\{שרש\|([^|}\s]+)/)
  return m1 ? m1[1].trim() : null
}
const KNOWN_SECTIONS = new Set(['גיזרון','נגזרות','מילים נרדפות','ניגודים','צירופים','מידע נוסף','ראו גם','הערות שוליים','תרגום'])
const KEEP_LANGS = new Set(['אנגלית','ערבית','ארמית'])

function parsePage(title, wikitext) {
  if (!wikitext || !containsHebrew(title)) return []
  if (/^#הפניה|^#REDIRECT/i.test(wikitext.trim())) return []
  const lines = wikitext.split('\n')
  const senses = []
  let cur = null, curSection = null, curDefIdx = -1
  function flush() {
    if (cur && (cur.definitions.length > 0 || Object.keys(cur.sections).length > 0)) senses.push(cur)
    cur = null; curSection = null; curDefIdx = -1
  }
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] || ''
    const senseMatch = line.match(/^==([^=][^=]*)==\s*$/)
    if (senseMatch) {
      flush()
      const rawHeader = (senseMatch[1] || '').replace(/\{\{[^}]*\}\}/g, '').trim()
      cur = { nikud: /[\u05B0-\u05C7]/.test(rawHeader) ? rawHeader : null, headword: stripNikud(rawHeader) || stripNikud(title), pos: null, binyan: null, shoresh: null, ktivMale: null, definitions: [], sections: {}, translations: [] }
      curSection = null; curDefIdx = -1; continue
    }
    if (!cur) continue
    if (line.includes('{{ניתוח דקדוקי')) {
      let block = line, j = i + 1
      let depth = (line.match(/\{\{/g)||[]).length - (line.match(/\}\}/g)||[]).length
      while (j < lines.length && depth > 0) { const nl = lines[j]||''; block += '\n'+nl; depth += (nl.match(/\{\{/g)||[]).length-(nl.match(/\}\}/g)||[]).length; j++ }
      i = j - 1
      cur.shoresh = cur.shoresh || extractShoresh(block)
      cur.binyan = cur.binyan || extractFromTemplate(block, 'בניין')
      cur.pos = cur.pos || extractFromTemplate(block, 'חלק דיבר')
      cur.ktivMale = cur.ktivMale || extractFromTemplate(block, 'כתיב מלא')
      continue
    }
    const secMatch = line.match(/^===([^=]+)===\s*$/)
    if (secMatch) {
      curSection = (secMatch[1]||'').trim(); curDefIdx = -1
      if (KNOWN_SECTIONS.has(curSection) && !cur.sections[curSection]) cur.sections[curSection] = []
      continue
    }
    if (/^====/.test(line)) { curSection = null; continue }
    if (!curSection && /^#{1,2}[^:#*]/.test(line)) {
      const layerMatch = line.match(/\{\{(?:מקרא|רובד|משלב)\|([^|}]+)/)
      const layer = layerMatch ? layerMatch[1].trim() : null
      const text = cleanWiki(line.replace(/^#+\s*/, ''))
      if (text && text.length > 1) { cur.definitions.push({ text, layer, examples: [] }); curDefIdx = cur.definitions.length - 1 }
      continue
    }
    if (!curSection && /^#[:#*]/.test(line) && curDefIdx >= 0) {
      const citMatch = line.match(/\{\{צט[^|]*\|([^|]+)\|([^|]+)\|([^|]+)\|([^|}]+)/)
      if (citMatch) { const def = cur.definitions[curDefIdx]; if (def) def.examples.push({ text: cleanWiki(citMatch[1]), source: `${citMatch[2]} ${citMatch[3]}, ${citMatch[4]}` }) }
      continue
    }
    if (curSection && cur.sections[curSection] !== undefined) {
      if (curSection === 'תרגום') {
        const langMatch = line.match(/^\*\s*([^:：]+)[：:]\s*(.+)/)
        if (langMatch && KEEP_LANGS.has(langMatch[1].trim())) {
          const words = [...(langMatch[2]||'').matchAll(/\{\{ת\|[^|]+\|([^|}]+)/g)].map(m => m[1].trim())
          if (words.length) cur.translations.push({ lang: langMatch[1].trim(), words })
        }
        continue
      }
      if (/^\*+/.test(line)) { const text = cleanWiki(line.replace(/^\*+\s*/, '')); if (text && containsHebrew(text) && text.length < 80) cur.sections[curSection].push(text); continue }
      if (line.trim() && !/^[={<[]/.test(line)) { const text = cleanWiki(line); if (text && text.length > 4) cur.sections[curSection].push(text) }
    }
  }
  flush()
  return senses
}

async function main() {
  const EXPORT = 'https://he.wiktionary.org/wiki/%D7%9E%D7%99%D7%95%D7%97%D7%93:%D7%99%D7%99%D7%A6%D7%95%D7%90'
  const body = new URLSearchParams({ action: 'submit', pages: 'שלום\nבית\nאמא', curonly: '1', wpDownload: '0' }).toString()
  const xml = await post(EXPORT, body)
  const pageRe = /<page>([\s\S]*?)<\/page>/g
  let m
  while ((m = pageRe.exec(xml)) !== null) {
    const titleMatch = m[1].match(/<title>([^<]+)<\/title>/)
    const textMatch = m[1].match(/<text[^>]*>([\s\S]*?)<\/text>/)
    if (!titleMatch || !textMatch) continue
    const unescape = s => s.replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#039;/g,"'")
    const title = unescape(titleMatch[1])
    const wikitext = unescape(textMatch[1])
    const senses = parsePage(title, wikitext)
    console.log(`\n=== ${title} === ${senses.length} senses`)
    senses.forEach((s, i) => {
      console.log(`  sense ${i}: headword=${s.headword} pos=${s.pos} shoresh=${s.shoresh} defs=${s.definitions.length} trans=${s.translations.length}`)
      if (s.definitions[0]) console.log(`    def[0]: [${s.definitions[0].layer||''}] ${s.definitions[0].text.substring(0,60)}`)
      if (s.translations[0]) console.log(`    trans[0]: ${s.translations[0].lang} = ${s.translations[0].words[0]}`)
    })
  }
}
main().catch(console.error)
