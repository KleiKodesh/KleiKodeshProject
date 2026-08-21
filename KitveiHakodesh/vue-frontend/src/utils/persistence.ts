/**
 * Storage driver. Moves bytes in and out of localStorage and IndexedDB.
 *
 * THE CONTRACT: this file knows nothing about the application. It has no schemas,
 * no retention policies, no key names, no reset workflow. If you can only explain a
 * change here by naming a feature, it does not belong in this file. It has no
 * imports, and that is the invariant worth keeping — the moment it needs one, it has
 * started knowing something it shouldn't.
 *
 * Things that used to live here and where they went, so they don't come back:
 *   schemas (TabState/BookState) → stores/tabStatePersistence.ts
 *   LastReadState + the 1000-book cap → stores/bookLastRead.ts
 *   Workspace types + workspace teardown → stores/workspaceStore.ts
 *   settings-vs-structural key policy → stores/settingsStore.ts
 *   the reset workflow → features/settings/appResetState.ts
 *   every key name → the module that owns the value (there is no central registry)
 *
 * Two halves, one reason to change each (the browser storage APIs):
 *
 *   localStorage — namespaced under `kitvei-hakodesh.` so app keys can never
 *   collide with the WebView2 host's or an addin's, JSON-coded, and total-error
 *   containing (localStorage throws on quota and in some privacy modes; every
 *   function here swallows that and degrades to null rather than propagating).
 *
 *   IndexedDB — a promise wrapper over IDB's callback API, plus one cached
 *   `IDBDatabase` handle per database per session. Each database is a flat
 *   key→blob bucket: one object store (`data`), out-of-line keys, no indexes.
 *   A caller needing in-line keys, secondary indexes or multiple object stores
 *   cannot use this driver and must hold its own handle — see
 *   `hebrewBooksHistoryStore`, which legitimately does.
 *
 * Callers name their own database and their own keys. Every localStorage key in the
 * app is namespaced `area.name` by its owning module, so the one flat namespace this
 * driver writes into cannot be claimed twice.
 */

const STORE = 'data'

// ── localStorage helpers (synchronous) ───────────────────────────────────────

const LS_PREFIX = 'kitvei-hakodesh.'

export function lsGet<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(LS_PREFIX + key)
    if (raw === null) return null
    return JSON.parse(raw) as T
  } catch { return null }
}

export function lsSet<T>(key: string, value: T): void {
  try { localStorage.setItem(LS_PREFIX + key, JSON.stringify(value)) } catch {}
}

export function lsDelete(key: string): void {
  try { localStorage.removeItem(LS_PREFIX + key) } catch {}
}

/** Every key in this app's namespace, unprefixed. Callers filter; the driver does not. */
export function lsKeys(): string[] {
  try {
    const out: string[] = []
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)
      if (k?.startsWith(LS_PREFIX)) out.push(k.slice(LS_PREFIX.length))
    }
    return out
  } catch { return [] }
}

/** Remove every key in this app's namespace. */
export function lsClearAll(): void {
  lsKeys().forEach(lsDelete)
}

// Unprefixed raw access, for the few values that must live outside the app
// namespace (e.g. a flag that has to be readable before the app boots). Same
// error containment; no JSON coding, since these are plain strings.

export function lsGetRaw(key: string): string | null {
  try { return localStorage.getItem(key) } catch { return null }
}

export function lsSetRaw(key: string, value: string): void {
  try { localStorage.setItem(key, value) } catch {}
}

export function lsDeleteRaw(key: string): void {
  try { localStorage.removeItem(key) } catch {}
}

// ── DB handles ────────────────────────────────────────────────────────────────

// Cache only — a name absent here is opened on demand and cached on first use.
// Listed names are the databases this driver owns; databases held by a store
// (app-hb-history, app-recently-opened) are deliberately not among them.
const handles: Record<string, IDBDatabase | null> = {
  'app-tabs': null,
  'app-recent-tabs': null,
  'app-lastread': null,
  'app-search-cache': null,
  'app-dict-cache': null,
  'app-catalog-toc-cache': null,
}

// In-flight opens, keyed by database name. The handle is only set in `onsuccess`, so two
// concurrent first-time calls for the same database used to open TWO connections and keep
// only the second — the orphan then blocked `dropDatabase` forever (its `onblocked` resolves
// silently, so a full app reset reported success while the database survived).
const opening: Record<string, Promise<IDBDatabase> | undefined> = {}
// Bumped by dropDatabase. An open already in flight when the database is dropped must not
// publish its connection afterwards: it would resurrect the handle we just cleared and hold
// the deleted database open.
const openEpoch: Record<string, number> = {}

function openDb(name: string): Promise<IDBDatabase> {
  if (handles[name]) return Promise.resolve(handles[name]!)
  const inFlight = opening[name]
  if (inFlight) return inFlight

  const epoch = openEpoch[name] ?? 0
  const open = new Promise<IDBDatabase>((resolve, reject) => {
    const req = indexedDB.open(name, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE)) req.result.createObjectStore(STORE)
    }
    req.onsuccess = () => {
      if ((openEpoch[name] ?? 0) !== epoch) {
        // Dropped while we were opening — close rather than publish.
        try { req.result.close() } catch { /* already closing */ }
        reject(new Error(`database ${name} was dropped while opening`))
        return
      }
      handles[name] = req.result
      resolve(req.result)
    }
    req.onerror = () => reject(req.error)
  }).finally(() => {
    if (opening[name] === open) delete opening[name]
  })
  opening[name] = open
  return open
}

// ── Core get / set / delete ───────────────────────────────────────────────────

export async function dbGet<T>(dbName: string, key: string): Promise<T | null> {
  const store = (await openDb(dbName)).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.get(key)
    req.onsuccess = () => resolve(req.result ?? null)
    req.onerror = () => reject(req.error)
  })
}

export async function dbSet<T>(dbName: string, key: string, value: T): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.put(value, key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

export async function dbDelete(dbName: string, key: string): Promise<void> {
  const store = (await openDb(dbName)).transaction(STORE, 'readwrite').objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.delete(key)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
  })
}

export async function dbDeleteByPrefix(dbName: string, prefix: string): Promise<void> {
  const idb = await openDb(dbName)
  return new Promise((resolve, reject) => {
    const req = idb.transaction(STORE, 'readwrite').objectStore(STORE).openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) {
        resolve()
        return
      }
      if ((cursor.key as string).startsWith(prefix)) cursor.delete()
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
}

/** Does this key exist? Cheaper than a get — reads the key, not the value. */
export async function dbHasKey(dbName: string, key: string): Promise<boolean> {
  const store = (await openDb(dbName)).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.getKey(key)
    req.onsuccess = () => resolve(req.result !== undefined)
    req.onerror = () => reject(req.error)
  })
}

/** Number of entries. For callers enforcing their own retention cap. */
export async function dbCount(dbName: string): Promise<number> {
  const store = (await openDb(dbName)).transaction(STORE).objectStore(STORE)
  return new Promise((resolve, reject) => {
    const req = store.count()
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

/**
 * Every entry as a [key, value] pair, in cursor order. For a caller that has to RANK
 * entries by something inside the value (a retention cap by recency, say) — cursor
 * order is lexicographic by key and says nothing about age.
 */
export async function dbListEntries<T>(dbName: string): Promise<Array<[string, T]>> {
  const idb = await openDb(dbName)
  return new Promise((resolve, reject) => {
    const acc: Array<[string, T]> = []
    const req = idb.transaction(STORE).objectStore(STORE).openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) {
        resolve(acc)
        return
      }
      acc.push([cursor.key as string, cursor.value as T])
      cursor.continue()
    }
    req.onerror = () => reject(req.error)
  })
}

/**
 * Close this driver's handle and delete the database.
 *
 * Only safe for databases this driver opened. `deleteDatabase` stalls on
 * `onblocked` while any handle is open, so a database whose handle is held
 * elsewhere must be dropped by its own owner.
 */
export function dropDatabase(name: string): Promise<void> {
  handles[name]?.close()
  handles[name] = null
  // An open still in flight would otherwise publish its handle after the delete and
  // resurrect the entry in `handles` — the epoch bump makes it close instead.
  openEpoch[name] = (openEpoch[name] ?? 0) + 1
  delete opening[name]
  return new Promise((resolve, reject) => {
    const req = indexedDB.deleteDatabase(name)
    req.onsuccess = () => resolve()
    req.onerror = () => reject(req.error)
    req.onblocked = () => resolve() // blocked means another tab holds it open; reload will finish the delete
  })
}

// ── Search cache DB ───────────────────────────────────────────────────────────

export function idbGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-search-cache', key)
}
export function idbSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-search-cache', key, value)
}
export function idbDelete(key: string): Promise<void> {
  return dbDelete('app-search-cache', key)
}
export function idbDeleteByPrefix(prefix: string): Promise<void> {
  return dbDeleteByPrefix('app-search-cache', prefix)
}

// ── Dictionary cache DB ──────────────────────────────────────────────────────

export function idbDictionaryCacheGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-dict-cache', key)
}
export function idbDictionaryCacheSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-dict-cache', key, value)
}
export function idbDictionaryCacheDelete(key: string): Promise<void> {
  return dbDelete('app-dict-cache', key)
}

// ── Catalog TOC search cache DB ───────────────────────────────────────────────

export function idbCatalogTocCacheGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-catalog-toc-cache', key)
}
export function idbCatalogTocCacheSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-catalog-toc-cache', key, value)
}
export function idbCatalogTocCacheDelete(key: string): Promise<void> {
  return dbDelete('app-catalog-toc-cache', key)
}

// ── Tabs DB ───────────────────────────────────────────────────────────────────

export function idbTabsGet<T>(key: string): Promise<T | null> {
  return dbGet<T>('app-tabs', key)
}
export function idbTabsSet<T>(key: string, value: T): Promise<void> {
  return dbSet('app-tabs', key, value)
}
export function idbTabsDelete(key: string): Promise<void> {
  return dbDelete('app-tabs', key)
}
export function idbTabsDeleteByPrefix(prefix: string): Promise<void> {
  return dbDeleteByPrefix('app-tabs', prefix)
}
