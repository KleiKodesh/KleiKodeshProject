/**
 * Otzaria personal-books (user_books.db) support for the HOSTED path.
 *
 * The hosted app ships raw SQL through __webviewQuery, so the host never learns
 * which query it is running and cannot route — routing happens HERE, before the
 * SQL is sent, mirroring the service's SeforimDb routing (dev needs none of this:
 * the service routes server-side and these helpers are never reached there).
 *
 * THE MODEL. Both databases number their rows independently, so personal-book ids
 * are shifted by USER_BOOKS_BASE at the app boundary; an id then carries its own
 * routing information. Library ids pass through untouched — the library path is
 * identical to the pre-personal-books behavior. Three query shapes (identical to
 * the service's classification — keep the two in sync):
 *   ROUTE       an inbound id picks the database; outbound ids shift back
 *   SPLIT-MERGE an inbound id LIST may span both (search results do)
 *   UNION       no inbound id (enumeration / title lookup); library block first
 *
 * CONNECTION TYPES are translated BY NAME, never shifted: both DBs assign
 * connection_type ids lazily in encounter order, so the same id means DIFFERENT
 * types per DB (real data: user 2=COMMENTARY vs library 2=SUPER_COMMENTARY).
 *
 * The DB belongs to ANOTHER APP: it appears/changes/disappears while we run, so
 * availability is re-probed on a TTL and `unavailable` replies read as empty.
 */

import { SQL } from './queries.sql'

declare global {
  interface Window {
    __webviewUserBooksQuery?: (
      sql: string,
      params: unknown[],
    ) => Promise<{ rows: unknown[]; unavailable?: boolean }>
    __webviewUserBooksInfo?: () => Promise<{ present: boolean; path: string | null }>
  }
}

// ── Corpus ids (mirror of the service's CorpusIds.cs) ─────────────────────────

/** First app-visible id belonging to the personal-books database. */
export const USER_BOOKS_BASE = 1_000_000_000

export const isUserBooksId = (appId: number): boolean => appId >= USER_BOOKS_BASE

/** The id as stored in its own database (identity for library ids). */
export const toLocalId = (appId: number): number =>
  appId >= USER_BOOKS_BASE ? appId - USER_BOOKS_BASE : appId

/** The app-visible id for a row read out of the personal-books database. */
export const toUserAppId = (localId: number): number => localId + USER_BOOKS_BASE

/**
 * Splits a mixed app-id list into per-corpus LOCAL id lists. Library group first —
 * union/merge call sites rely on that to preserve pre-personal-books ordering.
 */
export function groupByCorpus(appIds: number[]): { library: number[]; userBooks: number[] } {
  const library: number[] = []
  const userBooks: number[] = []
  for (const id of appIds) {
    if (isUserBooksId(id)) userBooks.push(id - USER_BOOKS_BASE)
    else library.push(id)
  }
  return { library, userBooks }
}

/**
 * Shifts the listed id columns of USER-DB result rows into the app-visible space.
 * null stays null and the 0 sentinel stays 0 — same convention as the service.
 */
export function shiftRowIds<T extends object>(rows: T[], keys: (keyof T)[]): T[] {
  return rows.map((row) => {
    const out = { ...row } as Record<string, unknown>
    for (const k of keys) {
      const v = out[k as string]
      if (typeof v === 'number' && v !== 0) out[k as string] = v + USER_BOOKS_BASE
    }
    return out as T
  })
}

// ── Availability + channel ────────────────────────────────────────────────────

const hasUserBooksChannel = (): boolean => typeof window.__webviewUserBooksQuery === 'function'

let _present: boolean | null = null
let _nextProbe = 0

/**
 * Whether user_books.db is present right now (TTL-cached probe — the file appears
 * when the user adds their first personal book in Otzaria, possibly mid-session).
 * False short-circuits every routed call site back to the plain library path.
 */
export async function userBooksPresent(): Promise<boolean> {
  if (!hasUserBooksChannel()) return false
  const now = Date.now()
  if (_present !== null && now < _nextProbe) return _present
  _nextProbe = now + 5_000
  try {
    _present = (await window.__webviewUserBooksInfo!()).present
  } catch {
    _present = false
  }
  if (_present === false) invalidateUserBooksCaches()
  return _present
}

/**
 * Raw variant that PRESERVES the unavailability signal. Everything that CACHES an
 * answer derived from a reply (schema probes, type maps) must use this and refuse
 * to cache on `unavailable` — an unavailable reply is indistinguishable from real
 * data through the rows alone ([] is a legitimate result), and caching one poisons
 * the session: a transient absence at first-probe time would pin the wrong SQL
 * variant / empty type maps long after the DB is back.
 */
async function queryUserBooksRaw<T = unknown>(
  sql: string,
  params: unknown[] = [],
): Promise<{ rows: T[]; unavailable: boolean }> {
  if (!hasUserBooksChannel()) return { rows: [], unavailable: true }
  const res = await window.__webviewUserBooksQuery!(sql, params)
  if (res.unavailable) {
    _present = false
    invalidateUserBooksCaches()
    return { rows: [], unavailable: true }
  }
  return { rows: res.rows as T[], unavailable: false }
}

/**
 * Raw SQL against user_books.db. `unavailable` (DB absent/deleted) reads as empty —
 * for rows that no longer exist, "no rows" IS the correct answer, not an error.
 */
export async function queryUserBooks<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  return (await queryUserBooksRaw<T>(sql, params)).rows
}

// ── Schema probes (per-DB — the library and Otzaria schemas answer differently) ──
//
// Each probe caches ONLY an answer computed from an AVAILABLE reply, and only when
// no invalidation interleaved with its await (generation guard) — otherwise it
// answers false for THIS call and re-probes next time. A cached wrong variant is a
// session-long silent failure (TOC that can't navigate); a one-call conservative
// answer is not.

let _generation = 0

// tocEntry.lineIndex: Otzaria-built DBs store the TOC position ON the entry (their
// line table is empty — user_books.db is a catalog+TOC index over files); the
// library derives it by joining line. Probe, don't assume.
let _tocHasLineIndex: boolean | null = null
export async function userTocHasLineIndex(): Promise<boolean> {
  if (_tocHasLineIndex !== null) return _tocHasLineIndex
  return probe(
    `SELECT COUNT(*) AS n FROM pragma_table_info('tocEntry') WHERE name = 'lineIndex'`,
    (v) => { _tocHasLineIndex = v },
  )
}

// link.targetLineIndex: absent in Otzaria-built DBs → the JOIN variant.
let _linkHasTargetLineIndex: boolean | null = null
export async function userLinkHasTargetLineIndex(): Promise<boolean> {
  if (_linkHasTargetLineIndex !== null) return _linkHasTargetLineIndex
  return probe(SQL.HAS_LINK_TARGET_LINE_INDEX, (v) => { _linkHasTargetLineIndex = v })
}

// link_anchor table: no Otzaria-built DB has it today.
let _hasLinkAnchor: boolean | null = null
export async function userHasLinkAnchor(): Promise<boolean> {
  if (_hasLinkAnchor !== null) return _hasLinkAnchor
  return probe(SQL.HAS_LINK_ANCHOR_TABLE, (v) => { _hasLinkAnchor = v })
}

async function probe(sql: string, cache: (v: boolean) => void): Promise<boolean> {
  const gen = _generation
  try {
    const res = await queryUserBooksRaw<{ n: number }>(sql)
    if (res.unavailable) return false // don't cache — re-probe when the DB is back
    const value = (res.rows[0]?.n ?? 0) > 0
    if (gen === _generation) cache(value)
    return value
  } catch {
    return false // unknown — don't cache, re-probe next call
  }
}

// ── Connection-type translation (by NAME, never by shift) ─────────────────────

type ConnTypeMaps = { idToName: Map<number, string>; nameToId: Map<string, number> }

let _userTypes: ConnTypeMaps | null = null
let _userTypesExpiry = 0
let _libTypes: ConnTypeMaps | null = null

function buildMaps(rows: { id: number; name: string }[]): ConnTypeMaps {
  const maps: ConnTypeMaps = { idToName: new Map(), nameToId: new Map() }
  for (const r of rows) {
    maps.idToName.set(r.id, r.name)
    if (r.name && !maps.nameToId.has(r.name)) maps.nameToId.set(r.name, r.id)
  }
  return maps
}

/**
 * The library's types ARE the app-visible type-id space. `libraryQuery` is injected
 * (rather than importing seforimDb) to keep this module free of import cycles.
 *
 * Throws when user_books.db is unavailable — rows can't be translated without maps,
 * and every caller sits behind a degrade-to-library-only guard. An unavailable-window
 * load is NEVER cached (see queryUserBooksRaw), and a load that raced an invalidation
 * is used for this call but not stored (generation guard). The user maps also expire
 * on a 60s TTL: Otzaria appends connection types lazily as links are created, and
 * unlike the service we have no file change-stamp to key on.
 */
export async function connTypeMaps(
  libraryQuery: (sql: string) => Promise<{ id: number; name: string }[]>,
): Promise<{ user: ConnTypeMaps; lib: ConnTypeMaps }> {
  let user = _userTypes
  if (user === null || Date.now() >= _userTypesExpiry) {
    const gen = _generation
    const res = await queryUserBooksRaw<{ id: number; name: string }>(SQL.GET_ALL_CONNECTION_TYPES)
    if (res.unavailable) throw new Error('personal-books DB unavailable')
    user = buildMaps(res.rows)
    if (gen === _generation) {
      _userTypes = user
      _userTypesExpiry = Date.now() + 60_000
    }
  }
  if (_libTypes === null) _libTypes = buildMaps(await libraryQuery(SQL.GET_ALL_CONNECTION_TYPES))
  return { user, lib: _libTypes }
}

/** USER-DB type id → app space: the library id of the SAME NAME, else shifted (user-only name). */
export function userTypeIdToApp(localTypeId: number, maps: { user: ConnTypeMaps; lib: ConnTypeMaps }): number {
  const name = maps.user.idToName.get(localTypeId)
  if (name !== undefined) {
    const libId = maps.lib.nameToId.get(name)
    if (libId !== undefined) return libId
  }
  return toUserAppId(localTypeId)
}

/**
 * App-visible type ids → the USER DB's local ids, DROPPING types that don't exist
 * there (a type id from one DB must never be sent verbatim to the other).
 * Empty result ⇒ skip the user corpus entirely.
 */
export function appTypeIdsToUserLocal(appTypeIds: number[], maps: { user: ConnTypeMaps; lib: ConnTypeMaps }): number[] {
  const local: number[] = []
  for (const appId of appTypeIds) {
    if (isUserBooksId(appId)) {
      const id = toLocalId(appId)
      if (maps.user.idToName.has(id)) local.push(id)
    } else {
      const name = maps.lib.idToName.get(appId)
      const userId = name === undefined ? undefined : maps.user.nameToId.get(name)
      if (userId !== undefined) local.push(userId)
    }
  }
  return local
}

/** Drops shifted user-only type ids for a LIBRARY query (identity in the common case). */
export function appTypeIdsToLibraryLocal(appTypeIds: number[]): number[] {
  return appTypeIds.some(isUserBooksId) ? appTypeIds.filter((id) => !isUserBooksId(id)) : appTypeIds
}

function invalidateUserBooksCaches(): void {
  // Schema generation is stable per file, but the FILE can be recreated by Otzaria
  // (types are appended lazily as links are created) — drop everything derived from
  // it whenever it disappears; the next presence probe rebuilds lazily. The bumped
  // generation stops any in-flight probe/load from re-caching its pre-invalidation
  // answer after its await resumes.
  _generation++
  _userTypes = null
  _userTypesExpiry = 0
  _tocHasLineIndex = null
  _linkHasTargetLineIndex = null
  _hasLinkAnchor = null
}
