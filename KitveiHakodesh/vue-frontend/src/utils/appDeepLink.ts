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
