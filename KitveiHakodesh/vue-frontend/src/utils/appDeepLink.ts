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
 * The installer registers the app as the Windows handler for the scheme
 * (Build/Installer/Helpers/ShellRegistrationHelper.cs RegisterProtocol), so clicking
 * such a link outside the app opens it. The link is URL-parseable (protocol
 * 'kitveihakodeshapp:', host 'book') and carries exactly the two values
 * openBookTarget needs — bookId and openTocLineIndex. Pasting one into the address
 * bar or the home search bar opens it too.
 */

/** Scheme of this app's own links. Was `seforimapp` — HostLink.cs still parses that. */
export const APP_LINK_SCHEME = 'kitveihakodeshapp'

export function buildLineLink(bookId: number, lineIndex: number): string {
  return `${APP_LINK_SCHEME}://book/${bookId}?index=${lineIndex}`
}

/**
 * A link into a library app found in content: Otzaria, Zayit or this app's own. The book is named by
 *   - bookId: its id in the library DB (Otzaria's, Zayit's and this app's DB share the ids)
 *   - title: its title (Otzaria's book:// and inline-link links), resolved by the catalogue
 *   - lineId alone (Zayit's seforim://line/<id> citations): the line's DB row id names the book too
 * `index` is a 0-based POSITIONAL line; `lineId` a DB line row id (Zayit); `headings` a TOC path
 * (Otzaria's book://<title>#<heading>#<sub-heading>). None of them = open at the top.
 * `otherLibrary`: an Otzaria link to a book of the user's own or an attached library
 * (?source=user / ?source=db:<slug>) - its id is not an id in this app's library.
 */
export interface ParsedBookLink {
  scheme: 'otzaria' | 'kitveihakodeshapp' | 'zayit'
  bookId?: number
  title?: string
  headings?: string[]
  index?: number
  lineId?: number
  otherLibrary?: boolean
}

/**
 * Every scheme of a library-app link: this app opens them itself and never hands them to the
 * system browser. Mirrors FastDocView's WordLibraryAppLink (its readers mark these as app links).
 */
export const LIBRARY_APP_SCHEMES: readonly string[] = ['otzaria', 'zayit', APP_LINK_SCHEME, 'seforimapp', 'book', 'seforim']

/** True for any link into a library app (by scheme only), whether or not this app can open its target. */
export function isLibraryAppLink(url: string): boolean {
  if (ZAYIT_WEB_OPEN.test(url.trim())) return true
  const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(url.trim())?.[1]?.toLowerCase()
  return scheme != null && LIBRARY_APP_SCHEMES.includes(scheme)
}

/** Zayit's web share page (website/public/open.html): https://zayitapp.com/open.html#book/<id>/line/<id> opens zayit://<fragment>. */
const ZAYIT_WEB_OPEN = /^https?:\/\/(?:www\.)?zayitapp\.com\/(?:[a-z]{2}\/)?open(?:\.html)?\/?#(.+)$/i

/** Decodes a query value ('+' is a space there). */
function decodeQuery(text: string): string {
  try {
    return decodeURIComponent(text.replace(/\+/g, ' '))
  } catch {
    return text
  }
}

/** Decodes a path segment or fragment ('+' stays itself). */
function decodeSegment(text: string): string {
  try {
    return decodeURIComponent(text)
  } catch {
    return text
  }
}

interface SplitLink {
  scheme: string
  /** The part after "scheme://" up to '?' / '#', split on '/' (empty segments dropped), not decoded. */
  segments: string[]
  query: Map<string, string | null>
  fragment: string
}

function splitLink(url: string): SplitLink | null {
  let text = url.trim()
  const web = ZAYIT_WEB_OPEN.exec(text)
  if (web) text = `zayit://${web[1]}`
  const head = /^([a-z][a-z0-9+.-]*):\/\/([\s\S]*)$/i.exec(text)
  if (!head) return null
  let rest = head[2]!
  let fragment = ''
  const hash = rest.indexOf('#')
  if (hash >= 0) {
    fragment = rest.slice(hash + 1)
    rest = rest.slice(0, hash)
  }
  let queryText = ''
  const question = rest.indexOf('?')
  if (question >= 0) {
    queryText = rest.slice(question + 1)
    rest = rest.slice(0, question)
  }
  const query = new Map<string, string | null>()
  for (const part of queryText.split('&')) {
    if (!part) continue
    const eq = part.indexOf('=')
    const key = decodeQuery(eq >= 0 ? part.slice(0, eq) : part).toLowerCase()
    if (!query.has(key)) query.set(key, eq >= 0 ? decodeQuery(part.slice(eq + 1)) : null)
  }
  return { scheme: head[1]!.toLowerCase(), segments: rest.split('/').filter((s) => s.length > 0), query, fragment }
}

/** Ids and indexes are 32-bit in the DB and in HostLink.cs (int): larger ones are not a book or line. */
const MAX_ID = 2147483647

function nonNegative(value: string | null | undefined): number | undefined {
  if (value == null || !/^\s*\d+\s*$/.test(value)) return undefined
  const n = Number(value)
  return n <= MAX_ID ? n : undefined
}

/** A book named by id (all digits) or by title (anything else, URL-decoded). */
function bookOf(segment: string): { bookId: number } | { title: string } | null {
  const decoded = decodeSegment(segment).trim()
  if (!decoded) return null
  if (/^\d+$/.test(decoded)) {
    const bookId = Number(decoded)
    return bookId > 0 && bookId <= MAX_ID ? { bookId } : null
  }
  return { title: decoded }
}

function headingsOf(fragment: string): string[] | undefined {
  const headings = fragment.split('#').map((part) => decodeSegment(part).trim()).filter((part) => part.length > 0)
  return headings.length ? headings : undefined
}

/** Otzaria's inline-link names the book by its file path: the title is the file name without .txt (html_link_handler.dart _getTitleFromPath). */
function titleFromPath(path: string): string {
  const name = path.split(/[\\/]/).pop() ?? ''
  return name.replace(/\.(txt|text)$/i, '').trim()
}

/**
 * Parses a library-app book link; the host's KitveiHakodeshLib/HostLink.cs TryParse reads the same
 * grammar. Schemes, hosts and nouns ignore case; trailing slashes and URL-encoding are accepted.
 *   otzaria://open/book/<id|title>[?index=<n>][&mark][&m=..][&q=..][&source=user|db:<slug>]
 *   otzaria://open/pdf/<id>[?index=<page>]          (the book at its top: a PDF page is not a line)
 *   otzaria://inline-link?path=<file path>&index=<1-based line>[&ref=..]
 *   book://<title>[#<heading>[#<sub-heading>...]]   (Otzaria's links inside books)
 *   kitveihakodeshapp://book/<id|title>[?index=<n>] (and the legacy seforimapp://)
 *   zayit://book/<id|title>[/line/<lineId>]         (also https://zayitapp.com/open.html#book/...)
 *   seforim://line/<lineId>                         (Zayit's citation links)
 * Sources: Otzaria lib/core/external_uri_router.dart, lib/utils/book_link_builder.dart,
 * lib/utils/text/html_link_handler.dart; Zayit core/deeplink/ZayitDeepLink.kt, RefLinkUriHandler.kt.
 * Null for anything else (a search link, a tool link, a web page).
 */
export function parseBookLink(url: string): ParsedBookLink | null {
  const link = splitLink(url)
  if (!link) return null
  const [first, second, third, fourth] = link.segments
  const noun = (segment: string | undefined) => segment?.toLowerCase()
  switch (link.scheme) {
    case 'otzaria': {
      if (noun(first) === 'inline-link' && link.segments.length === 1) {
        const title = titleFromPath(link.query.get('path') ?? '')
        if (!title) return null
        const oneBased = nonNegative(link.query.get('index'))
        return { scheme: 'otzaria', title, ...(oneBased != null && oneBased > 0 ? { index: oneBased - 1 } : {}) }
      }
      if (noun(first) !== 'open' || link.segments.length !== 3) return null
      const kind = noun(second)
      if (kind !== 'book' && kind !== 'pdf') return null
      const book = bookOf(third!)
      if (!book) return null
      const source = link.query.get('source')?.trim().toLowerCase()
      const otherLibrary = source != null && source !== '' && source !== 'official'
      const index = kind === 'book' ? nonNegative(link.query.get('index')) : undefined
      return { scheme: 'otzaria', ...book, ...(index != null ? { index } : {}), ...(otherLibrary ? { otherLibrary } : {}) }
    }
    case 'book': {
      // book://<title>: the whole host-and-path is the title (a title may hold a '/')
      const title = link.segments.map(decodeSegment).join('/').trim()
      if (!title) return null
      const headings = headingsOf(link.fragment)
      return { scheme: 'otzaria', title, ...(headings ? { headings } : {}) }
    }
    case APP_LINK_SCHEME:
    case 'seforimapp': {
      if (noun(first) !== 'book' || link.segments.length !== 2) return null
      const book = bookOf(second!)
      if (!book) return null
      const index = nonNegative(link.query.get('index'))
      return { scheme: 'kitveihakodeshapp', ...book, ...(index != null ? { index } : {}) }
    }
    case 'zayit': {
      if (noun(first) !== 'book' || second == null) return null
      const book = bookOf(second)
      if (!book) return null
      if (link.segments.length === 2) return { scheme: 'zayit', ...book }
      const lineId = noun(third) === 'line' && link.segments.length === 4 ? nonNegative(fourth) : undefined
      return lineId != null && lineId > 0 ? { scheme: 'zayit', ...book, lineId } : null
    }
    case 'seforim': {
      const lineId = noun(first) === 'line' && link.segments.length === 2 ? nonNegative(second) : undefined
      return lineId != null && lineId > 0 ? { scheme: 'zayit', lineId } : null
    }
    default:
      return null
  }
}

/** A search link's query: otzaria://open/search?q=<text> or zayit://search/<text>; null for anything else. */
export function parseSearchLink(url: string): string | null {
  const link = splitLink(url)
  if (!link) return null
  const [first, second] = link.segments
  let query: string | null | undefined
  if (link.scheme === 'otzaria' && first?.toLowerCase() === 'open' && second?.toLowerCase() === 'search' && link.segments.length === 2)
    query = link.query.get('q')
  else if (link.scheme === 'zayit' && first?.toLowerCase() === 'search' && link.segments.length >= 2)
    query = decodeQuery(link.segments.slice(1).join('/'))
  return query?.trim() || null
}
