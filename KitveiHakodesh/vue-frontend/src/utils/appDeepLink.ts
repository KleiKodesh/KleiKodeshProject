/**
 * The app's own deep-link format — the one place the scheme and the URL shape are
 * spelled out. Every module that builds a link into the app calls `buildLineLink`,
 * so changing the format is a one-line change here; the C# side that parses such
 * links back has the matching single definition in `KitveiHakodeshLib/HostLink.cs`
 * (`HostLink.AppScheme`). Never hand-write the scheme anywhere else.
 *
 *   kitveihakodeshapp://book/<bookId>?index=<lineIndex>
 *
 * Mirrors the otzaria:// links this app already parses (`otzaria://open/book/<bookId>
 * ?index=<lineIndex>`) — same noun/id path with the locator as an `index` query
 * parameter, and `index` means the same thing in both: a 0-based POSITIONAL line
 * index, not a database row id. Keeping the shapes aligned means one code path parses
 * them, and leaves room to grow the same way Otzaria did (it adds `&mark` / `&m=<text>`
 * for highlighting).
 *
 * Query parameter rather than a path segment or a `label:value` pair, deliberately:
 *   - `book:<id>` inside the authority is parsed as a PORT, so the id lands in
 *     url.port and any id above 65535 makes the URL throw outright.
 *   - a bare `:` in a path segment parses, but some link detectors in chat and mail
 *     clients end an auto-linked URL at the punctuation and truncate it.
 *
 * The app does NOT register itself as a handler for the scheme, so clicking such a
 * link outside the app does nothing today. The format exists so links copied now keep
 * working if a handler is ever registered: it is URL-parseable (protocol
 * 'kitveihakodeshapp:', host 'book') and carries exactly the two values
 * openBookTarget needs — bookId and openTocLineIndex.
 */

/** Scheme of this app's own links. Was `seforimapp` — HostLink.cs still parses that. */
export const APP_LINK_SCHEME = 'kitveihakodeshapp'

export function buildLineLink(bookId: number, lineIndex: number): string {
  return `${APP_LINK_SCHEME}://book/${bookId}?index=${lineIndex}`
}

/** A book link found in content: an otzaria:// or this app's link (positional `index`), or zayit:// (DB `lineId`). */
export interface ParsedBookLink {
  scheme: 'otzaria' | 'kitveihakodeshapp' | 'zayit'
  bookId: number
  index?: number
  lineId?: number
}

const OTZARIA_LINK = /^otzaria:\/\/open\/book\/(\d+)\/?(?:\?(.*))?$/i
const APP_LINK = new RegExp(String.raw`^(?:${APP_LINK_SCHEME}|seforimapp)://book/(\d+)/?(?:\?(.*))?$`, 'i')
const ZAYIT_LINK = /^zayit:\/\/book\/(\d+)\/line\/(\d+)$/i

/**
 * Parses a book link the way the host does (KitveiHakodeshLib/HostLink.cs TryParse, the same
 * grammar): otzaria://open/book/<id>?index=<n>, kitveihakodeshapp://book/<id>?index=<n> (or the
 * legacy seforimapp://), zayit://book/<id>/line/<lineId>. An indexed link needs a non-negative
 * index. Null for anything else.
 */
export function parseBookLink(url: string): ParsedBookLink | null {
  const text = url.trim()
  const indexed = OTZARIA_LINK.exec(text) ?? APP_LINK.exec(text)
  if (indexed) {
    const bookId = Number(indexed[1])
    let index: number | undefined
    for (const part of (indexed[2] ?? '').split('&')) {
      const [key, value] = part.split('=')
      if (key?.toLowerCase() === 'index' && value != null && /^\d+$/.test(value)) index = Number(value)
    }
    if (index == null || !Number.isSafeInteger(bookId)) return null
    return { scheme: OTZARIA_LINK.test(text) ? 'otzaria' : 'kitveihakodeshapp', bookId, index }
  }
  const zayit = ZAYIT_LINK.exec(text)
  if (zayit) return { scheme: 'zayit', bookId: Number(zayit[1]), lineId: Number(zayit[2]) }
  return null
}
