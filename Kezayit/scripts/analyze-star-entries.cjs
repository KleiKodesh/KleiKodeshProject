'use strict'
const fs = require('fs')
const iconv = require('iconv-lite')

const raw = fs.readFileSync('C:\\Users\\Admin\\Documents\\ToratEmetInstall\\Dictionaries\\FinalDictionary.txt')
const text = iconv.decode(raw, 'win1255')
const lines = text.split(/\r?\n/).filter(l => /^[0-9] /.test(l))

const starEntries = lines.filter(l => l.match(/^[0-9] .+=\*$/))
const normalEntries = lines.filter(l => !l.match(/^[0-9] .+=\*$/))

// Build abbreviation lookup (normalized)
const abbrevMap = new Map()
for (const line of normalEntries) {
  const m = line.match(/^[0-9] (.+?)=(.+)$/)
  if (!m) continue
  const hw = m[1].trim().replace(/''/g, '"')
  const def = m[2].trim()
  if (!abbrevMap.has(hw)) abbrevMap.set(hw, [])
  abbrevMap.get(hw).push(def)
}

// Hebrew preposition prefixes that can be stripped
const PREFIXES = ['ב', 'ל', 'כ', 'מ', 'ו', 'ש', 'ה', 'ד', 'מב', 'מל', 'וב', 'ול', 'שב', 'של', 'הב', 'כב', 'כל']

/**
 * Try to resolve a headword to its abbreviation expansion.
 * Returns { prefix, abbrev, expansions } or null.
 *
 * Algorithm:
 * 1. Exact match
 * 2. Strip known Hebrew preposition prefixes, then exact match
 * 3. Find the geresh/gershayim position, try all suffix splits
 * 4. Strip prefix + try suffix splits
 */
function resolve(hw) {
  // 1. Exact
  if (abbrevMap.has(hw)) return { prefix: '', abbrev: hw, expansions: abbrevMap.get(hw) }

  // 2. Strip prefix, exact match
  for (const pfx of PREFIXES) {
    if (hw.startsWith(pfx) && hw.length > pfx.length) {
      const rest = hw.slice(pfx.length)
      if (abbrevMap.has(rest)) return { prefix: pfx, abbrev: rest, expansions: abbrevMap.get(rest) }
    }
  }

  // 3. Find geresh position, try suffix splits
  const gereshPos = hw.indexOf('"')
  if (gereshPos > 0) {
    // Try from one char before " up to the full string
    for (let i = Math.max(0, gereshPos - 1); i >= 0; i--) {
      const suffix = hw.slice(i)
      if (abbrevMap.has(suffix)) {
        return { prefix: hw.slice(0, i), abbrev: suffix, expansions: abbrevMap.get(suffix) }
      }
    }
  }

  // 4. Strip prefix + geresh suffix splits
  for (const pfx of PREFIXES) {
    if (!hw.startsWith(pfx)) continue
    const rest = hw.slice(pfx.length)
    const gp = rest.indexOf('"')
    if (gp > 0) {
      for (let i = Math.max(0, gp - 1); i >= 0; i--) {
        const suffix = rest.slice(i)
        if (abbrevMap.has(suffix)) {
          return { prefix: pfx + rest.slice(0, i), abbrev: suffix, expansions: abbrevMap.get(suffix) }
        }
      }
    }
  }

  return null
}

let resolved = 0, unresolved = 0
const results = []
const unresolvedList = []

for (const line of starEntries) {
  const m = line.match(/^[0-9] (.+?)=\*$/)
  if (!m) continue
  const hw = m[1].trim().replace(/''/g, '"')
  const r = resolve(hw)
  if (r) {
    resolved++
    results.push({ hw, prefix: r.prefix, abbrev: r.abbrev, expansions: r.expansions })
  } else {
    unresolved++
    unresolvedList.push(hw)
  }
}

console.log(`Resolved: ${resolved} / ${starEntries.length}`)
console.log(`Unresolved: ${unresolved}`)

console.log('\n── Sample resolved ──')
results.slice(0, 20).forEach(r =>
  console.log(`  [${r.hw}] prefix="${r.prefix}" abbrev="${r.abbrev}" -> ${r.expansions.join(' | ')}`)
)

console.log('\n── Unresolved (all) ──')
unresolvedList.forEach(s => console.log(`  [${s}]`))
