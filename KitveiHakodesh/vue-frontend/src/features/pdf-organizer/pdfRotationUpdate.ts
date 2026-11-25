import { getDocument } from 'pdfjs-dist'

/**
 * Turns pages of a PDF by appending one incremental update: each turned page's dictionary is
 * written again with a new /Rotate, followed by an uncompressed cross-reference stream.
 * PDF.js's page editor (extractPages) has no rotation, so the organizer turns pages after it.
 *
 * The page's dictionary is read from the file itself: a plain "n g obj" or an entry of an
 * object stream (FlateDecode, as PDF.js writes them). The page keeps its object number, so
 * strings in an encrypted file stay valid, and nothing else in the file changes.
 */

const enc = (s: string): Uint8Array => {
  const out = new Uint8Array(s.length)
  for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i) & 0xff
  return out
}
function latin1(bytes: Uint8Array): string {
  let s = ''
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return s
}

/** End (exclusive) of the PDF value starting at `start` - a dictionary, array, string or plain token. */
function valueEnd(text: string, start: number): number {
  let depth = 0
  let i = start
  for (; i < text.length; i++) {
    const c = text[i]!
    if (c === '%') { while (i < text.length && text[i] !== '\n' && text[i] !== '\r') i++; continue }
    if (c === '(') {
      let nest = 1
      for (i++; i < text.length && nest; i++) {
        if (text[i] === '\\') i++
        else if (text[i] === '(') nest++
        else if (text[i] === ')') nest--
      }
      i--
      if (!depth) return i + 1
      continue
    }
    if (c === '<' && text[i + 1] === '<') { depth++; i++; continue }
    if (c === '>' && text[i + 1] === '>') { depth--; i++; if (!depth) return i + 1; continue }
    if (c === '<') { i = text.indexOf('>', i); if (!depth) return i + 1; continue }
    if (c === '[') { depth++; continue }
    if (c === ']') { depth--; if (!depth) return i + 1; continue }
    if (!depth && i > start && /[\s/]/.test(c)) return i
  }
  return i
}

function dictAt(text: string, from: number): string | null {
  const open = text.indexOf('<<', from)
  if (open < 0) return null
  return text.slice(open, valueEnd(text, open))
}

async function inflate(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as BlobPart]).stream().pipeThrough(new DecompressionStream('deflate'))
  return new Uint8Array(await new Response(stream).arrayBuffer())
}

/** A direct integer, or an indirect one resolved from a plain object. */
function intValue(text: string, dict: string, key: string): number | null {
  const ref = new RegExp(`/${key}\\s+(\\d+)\\s+(\\d+)\\s+R`).exec(dict)
  if (ref) {
    const m = [...text.matchAll(new RegExp(`(?:^|\\s)${ref[1]}\\s+${ref[2]}\\s+obj\\s+(\\d+)`, 'g'))].pop()
    return m ? Number(m[1]) : null
  }
  const direct = new RegExp(`/${key}\\s+(\\d+)`).exec(dict)
  return direct ? Number(direct[1]) : null
}

/** Every object held in an object stream, by number (later streams win). */
async function objectStreamObjects(bytes: Uint8Array, text: string): Promise<Map<number, string>> {
  const objects = new Map<number, string>()
  const re = /(\d+)\s+(\d+)\s+obj\s*<</g
  for (let m = re.exec(text); m; m = re.exec(text)) {
    const dictStart = m.index + m[0].length - 2
    const end = valueEnd(text, dictStart)
    const dict = text.slice(dictStart, end)
    re.lastIndex = end
    if (!/\/Type\s*\/ObjStm\b/.test(dict)) continue
    const streamKw = /^\s*stream\r?\n/.exec(text.slice(end, end + 16))
    const length = intValue(text, dict, 'Length')
    if (!streamKw || length == null) continue
    const dataStart = end + streamKw[0].length
    let data = bytes.subarray(dataStart, dataStart + length)
    const filter = /\/Filter\s*\[?\s*\/(\w+)/.exec(dict)?.[1]
    if (filter === 'FlateDecode') {
      if (/\/DecodeParms/.test(dict)) continue // predictors: not written by PDF.js for object streams
      try { data = await inflate(data) } catch { continue }
    } else if (filter) continue
    const n = Number(/\/N\s+(\d+)/.exec(dict)?.[1])
    const first = Number(/\/First\s+(\d+)/.exec(dict)?.[1])
    if (!Number.isFinite(n) || !Number.isFinite(first)) continue
    const body = latin1(data)
    const header = body.slice(0, first).trim().split(/\s+/).map(Number)
    for (let k = 0; k < n; k++) {
      const num = header[2 * k]!
      const off = first + header[2 * k + 1]!
      const next = k + 1 < n ? first + header[2 * k + 3]! : body.length
      objects.set(num, body.slice(off, next).trim())
    }
  }
  return objects
}

/**
 * Returns `bytes` with the pages at the given 0-based indexes turned by the given extra
 * degrees (clockwise) on top of their own rotation.
 */
export async function setPageRotations(bytes: Uint8Array, turns: Map<number, number>): Promise<Uint8Array> {
  // Page objects and their current (inherited) rotation, from PDF.js.
  const doc = await getDocument({ data: bytes.slice() }).promise
  const targets: { num: number; gen: number; rotate: number }[] = []
  try {
    for (const [index, turn] of turns) {
      const page = await doc.getPage(index + 1)
      const ref = page.ref
      if (!ref) throw new Error(`page ${index + 1} has no object`)
      targets.push({ num: ref.num, gen: ref.gen, rotate: (((page.rotate + turn) % 360) + 360) % 360 })
    }
  } finally {
    void doc.loadingTask.destroy()
  }

  const text = latin1(bytes)
  let streamObjects: Map<number, string> | null = null
  const parts: { num: number; gen: number; body: string }[] = []
  for (const t of targets) {
    let dict: string | null = null
    const plain = [...text.matchAll(new RegExp(`(?:^|[\\s>])${t.num}\\s+${t.gen}\\s+obj\\b`, 'g'))].pop()
    if (plain) dict = dictAt(text, plain.index! + plain[0].length)
    if (!dict) {
      streamObjects ??= await objectStreamObjects(bytes, text)
      const obj = streamObjects.get(t.num)
      dict = obj ? dictAt(obj, 0) : null
    }
    if (!dict || !/\/Type\s*\/Page\b/.test(dict)) throw new Error(`page object ${t.num} not found`)
    const body = `<</Rotate ${t.rotate}` + dict.slice(2).replace(/\/Rotate\s+-?\d+(?:\.\d+)?/g, '')
    parts.push({ num: t.num, gen: t.gen, body })
  }

  // The last cross-reference section: its trailer (or xref stream dictionary) gives Root, Size, ...
  const startxref = [...text.matchAll(/startxref\s+(\d+)/g)].pop()
  if (!startxref) throw new Error('no startxref')
  const prev = Number(startxref[1])
  const trailer = /^xref/.test(text.slice(prev, prev + 4))
    ? dictAt(text, text.indexOf('trailer', prev))
    : dictAt(text, prev)
  if (!trailer) throw new Error('no trailer')
  const pick = (re: RegExp) => re.exec(trailer)?.[0] ?? ''
  const root = pick(/\/Root\s+\d+\s+\d+\s+R/)
  if (!root) throw new Error('no /Root')
  const size = Number(/\/Size\s+(\d+)/.exec(trailer)?.[1])
  if (!Number.isFinite(size)) throw new Error('no /Size')

  // Append: the pages' new dictionaries, then an uncompressed xref stream (object `size`).
  const chunks: Uint8Array[] = [bytes]
  let offset = bytes.length
  const push = (s: string | Uint8Array) => {
    const b = typeof s === 'string' ? enc(s) : s
    chunks.push(b)
    offset += b.length
  }
  if (bytes[bytes.length - 1] !== 0x0a) push('\n')
  const entries: { num: number; gen: number; offset: number }[] = []
  for (const p of parts.sort((a, b) => a.num - b.num)) {
    entries.push({ num: p.num, gen: p.gen, offset })
    push(`${p.num} ${p.gen} obj\n${p.body}\nendobj\n`)
  }
  const xrefNum = size
  const xrefOffset = offset
  entries.push({ num: xrefNum, gen: 0, offset: xrefOffset })
  const row = (e: { gen: number; offset: number }) => [1, (e.offset >>> 24) & 0xff, (e.offset >>> 16) & 0xff, (e.offset >>> 8) & 0xff, e.offset & 0xff, (e.gen >>> 8) & 0xff, e.gen & 0xff]
  const data = new Uint8Array(entries.flatMap(row))
  const index = entries.map((e) => `${e.num} 1`).join(' ')
  const dict = `<</Type /XRef /Size ${size + 1} /W [1 4 2] /Index [${index}] ${root} ${pick(/\/Info\s+\d+\s+\d+\s+R/)} ${pick(/\/Encrypt\s+\d+\s+\d+\s+R/)} ${pick(/\/ID\s*\[[^\]]*\]/)} /Prev ${prev} /Length ${data.length}>>`
  push(`${xrefNum} 0 obj\n${dict}\nstream\n`)
  push(data)
  push(`\nendstream\nendobj\nstartxref\n${xrefOffset}\n%%EOF\n`)

  const out = new Uint8Array(offset)
  let at = 0
  for (const c of chunks) { out.set(c, at); at += c.length }
  return out
}
