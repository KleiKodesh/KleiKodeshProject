/**
 * OCJ-1 — the canonical JSON encoding the report collector digests.
 *
 * The COLLECTOR RECOMPUTES this digest and rejects a mismatch outright, so this is a
 * byte-for-byte port of its src/lib/corrections/ocj1.js rather than a reasonable-looking
 * equivalent: keys sorted, no whitespace, safe integers only, the same escape set, and
 * the same refusals. Verified byte-identical against that file, including Hebrew with
 * niqqud, quotes and astral characters. A difference here is invisible locally and
 * arrives as a 400 'digest_mismatch' on every single report.
 */

/** The largest integer JS and Dart both represent exactly. */
const MAX_SAFE = 9007199254740991

function encodeString(value: string): string {
  let out = '"'
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i)
    // A lone surrogate has no UTF-8 encoding; OCJ-1 rejects rather than replaces it.
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(i + 1)
      if (!(next >= 0xdc00 && next <= 0xdfff)) throw new Error('OCJ-1: lone high surrogate')
      out += value.slice(i, i + 2)
      i++
      continue
    }
    if (code >= 0xdc00 && code <= 0xdfff) throw new Error('OCJ-1: lone low surrogate')

    // Each of these appends the two-character ESCAPE, not the control character.
    if (code === 0x22) out += '\\"'
    else if (code === 0x5c) out += '\\\\'
    else if (code === 0x08) out += '\\b'
    else if (code === 0x0c) out += '\\f'
    else if (code === 0x0a) out += '\\n'
    else if (code === 0x0d) out += '\\r'
    else if (code === 0x09) out += '\\t'
    else if (code < 0x20) out += '\\u' + code.toString(16).padStart(4, '0')
    else out += value[i]
  }
  return out + '"'
}

export function canonicalJsonEncode(value: unknown): string {
  // `undefined` is REFUSED, not coerced. The collector's encoder throws on it, and
  // quietly turning it into null here would produce a digest it cannot reproduce —
  // a mismatch that only ever shows up as a 400 from the live server.
  if (value === undefined) throw new Error('OCJ-1: undefined is not encodable')
  if (value === null) return 'null'
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'number') {
    if (!Number.isInteger(value)) throw new Error('OCJ-1 allows integers only')
    if (Math.abs(value) > MAX_SAFE) throw new Error('OCJ-1 allows safe integers only')
    return String(value)
  }
  if (typeof value === 'string') return encodeString(value)
  if (Array.isArray(value)) return '[' + value.map(canonicalJsonEncode).join(',') + ']'
  if (typeof value === 'object') {
    // Plain objects only, matching the collector: a Date or a Map would otherwise
    // encode as {} and digest as something neither side intended.
    const proto = Object.getPrototypeOf(value)
    if (proto !== Object.prototype && proto !== null) throw new Error('OCJ-1: plain objects only')
    const entries = Object.entries(value as Record<string, unknown>)
    // Sorted by UTF-16 code unit, matching the collector's own compareUtf16.
    entries.sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    return '{' + entries.map(([k, v]) => encodeString(k) + ':' + canonicalJsonEncode(v)).join(',') + '}'
  }
  throw new Error(`OCJ-1 cannot encode ${typeof value}`)
}

/** Lowercase hex SHA-256 of the canonical encoding, as the collector expects. */
export async function canonicalJsonSha256(value: unknown): Promise<string> {
  const bytes = new TextEncoder().encode(canonicalJsonEncode(value))
  const hash = await crypto.subtle.digest('SHA-256', bytes)
  return [...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, '0')).join('')
}
