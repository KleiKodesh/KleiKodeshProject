/**
 * Decodes raw text-file bytes to a string, detecting the encoding instead of
 * assuming UTF-8. Many Hebrew .txt files are legacy Windows-1255 (Hebrew ANSI);
 * decoding those as UTF-8 fills the view with U+FFFD replacement chars (◇?).
 *
 * Strategy (mirrors the hosted C# path in LocalFileHandler.ReadTextDetectEncoding):
 *   1. Honor a BOM if present (UTF-8 / UTF-32 LE / UTF-32 BE / UTF-16 LE / UTF-16 BE).
 *      UTF-32 is checked before UTF-16 because the UTF-32 LE BOM (FF FE 00 00)
 *      starts with the UTF-16 LE BOM (FF FE) — order matters. TextDecoder has no
 *      UTF-32 support, so those two cases are decoded by hand.
 *   2. Otherwise, if the bytes are valid UTF-8, decode as UTF-8.
 *   3. Otherwise fall back to Windows-1255.
 */
export function decodeTextDetectEncoding(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer)

  // 1. BOM sniffing — a BOM is authoritative. Check longer BOMs first.
  if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf)
    return new TextDecoder('utf-8').decode(bytes.subarray(3))
  if (bytes.length >= 4 && bytes[0] === 0xff && bytes[1] === 0xfe && bytes[2] === 0x00 && bytes[3] === 0x00)
    return decodeUtf32(bytes.subarray(4), true)
  if (bytes.length >= 4 && bytes[0] === 0x00 && bytes[1] === 0x00 && bytes[2] === 0xfe && bytes[3] === 0xff)
    return decodeUtf32(bytes.subarray(4), false)
  if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe)
    return new TextDecoder('utf-16le').decode(bytes.subarray(2))
  if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff)
    return new TextDecoder('utf-16be').decode(bytes.subarray(2))

  // 2. No BOM: if the bytes are valid UTF-8, decode as UTF-8; else Windows-1255.
  //    TextDecoder with fatal:true throws on the first ill-formed sequence.
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes)
  } catch {
    return new TextDecoder('windows-1255').decode(bytes)
  }
}

/**
 * Decodes a UTF-32 byte body (BOM already stripped) by hand — TextDecoder does
 * not support UTF-32. Reads 4 bytes per code point in the given endianness.
 * Trailing bytes that don't form a full unit are ignored.
 */
function decodeUtf32(body: Uint8Array, littleEndian: boolean): string {
  const view = new DataView(body.buffer, body.byteOffset, body.byteLength)
  const codePoints: number[] = []
  for (let i = 0; i + 4 <= body.byteLength; i += 4) {
    codePoints.push(view.getUint32(i, littleEndian))
  }
  return String.fromCodePoint(...codePoints)
}
