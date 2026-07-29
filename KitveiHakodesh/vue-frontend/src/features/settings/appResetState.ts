import { ref } from 'vue'
import {
  lsClearAll,
  lsGetRaw,
  lsSetRaw,
  lsDeleteRaw,
  dropDatabase,
} from '@/utils/persistence'
import { resetHostApp } from '@/webview-host/bridge'

/** Set to true just before a reset/reload to block all interaction until the page reloads. */
export const resetting = ref(false)

/**
 * Marks a reset as started, so a crash mid-wipe can be recovered on the next boot.
 * Unprefixed on purpose: it must be readable before anything else initialises.
 */
const RESET_LS_KEY = '__pendingReset'

/**
 * Full app reset: wipe every local database and localStorage key, then reset the
 * host (FTS index, C# settings) and reload into a clean state.
 *
 * Lives here rather than in a store because it belongs to no single domain — it
 * wipes all seven databases plus localStorage, so no one store owns it. It used to
 * hang off `tabStore`, which had nothing to do with any of it.
 *
 * It also does not belong in the storage driver, which is where the wipe itself used
 * to live: this is not one operation but a sequencer across four subsystems —
 * IndexedDB, localStorage, the C# host, and the page lifecycle. A sequencer has to
 * reach every participant, so putting it at the bottom of the stack forced the
 * bottom to reach up (the driver imported two Pinia stores to finish the job).
 *
 * `scheduleReset()` first is the crash-safety net: it writes the `__pendingReset`
 * flag, and `checkAndExecPendingReset()` at boot redoes the wipe if the flag is
 * still set — i.e. if we died partway through. The wipe clears the flag itself, and
 * only after every database is actually gone.
 */
export async function resetEverything(): Promise<void> {
  resetting.value = true
  scheduleReset()
  await clearAllLocalStorage()
  await resetHostApp()
}

/** Schedule a full reset on next boot — synchronous localStorage write, zero IDB cost. */
export function scheduleReset(): void {
  lsSetRaw(RESET_LS_KEY, '1')
}

/**
 * Sweep any `app-` database the explicit list above did not name.
 *
 * Exists for the per-addin `app-addin-storage-<addinId>` databases, whose names are
 * only known at runtime, so a reset previously left third-party addin data behind —
 * the one kind of data a user resetting the app most likely wants gone.
 *
 * `indexedDB.databases()` is Chromium-only, which is fine (the app only ever runs in
 * WebView2 or a Chromium dev browser) but it is still feature-detected: an absent API
 * means the named drops above were the whole wipe, which is the previous behaviour
 * rather than a failure. The `app-` prefix keeps the sweep to this app's own
 * databases — the origin is shared with anything else the host loads.
 */
async function dropRemainingAppDatabases(): Promise<void> {
  if (typeof indexedDB.databases !== 'function') return
  try {
    const names = (await indexedDB.databases())
      .map((entry) => entry.name)
      .filter((name): name is string => !!name && name.startsWith('app-'))
    await Promise.all(names.map((name) => dropDatabase(name)))
  } catch {
    // Enumeration is a best-effort sweep on top of the explicit drops; if it fails the
    // named databases are already gone and the reset should still complete.
  }
}

/**
 * Drop every local database, then every localStorage key.
 *
 * The named databases are listed explicitly and the two store-owned ones are reached
 * by dynamic import, rather than read from a registry. That is deliberate:
 * `recentlyOpenedStore` is lazy-loaded, so a registry populated at module load would
 * silently MISS its database whenever the user never opened the recent list.
 *
 * The explicit list cannot cover everything, though — `useOtzariaAddinBridge` creates
 * one `app-addin-storage-<addinId>` database per addin, with names only known at
 * runtime. Those are swept afterwards by enumeration, which also catches any future
 * `app-` database whose author forgets to add it here.
 */
async function clearAllLocalStorage(): Promise<void> {
  // These two stores own their own IDB handles, and a handle must be closed before
  // deleteDatabase or it stalls on onblocked. Imported lazily so a reset never
  // forces the lazy-loaded store to initialise earlier than it otherwise would.
  const { dropHbHistoryDb } = await import('@/stores/hebrewBooksHistoryStore')
  const { dropRecentlyOpenedDb } = await import('@/stores/recentlyOpenedStore')
  await Promise.all([
    dropDatabase('app-tabs'),
    dropDatabase('app-lastread'),
    dropHbHistoryDb(),
    dropRecentlyOpenedDb(),
    dropDatabase('app-search-cache'),
    dropDatabase('app-dict-cache'),
    dropDatabase('app-catalog-toc-cache'),
  ])
  await dropRemainingAppDatabases()
  // MUST come last. These two clear the __pendingReset flag, so clearing it before
  // the drops finish would disarm the crash-safety net: die mid-wipe and the next
  // boot sees no flag and never redoes it. Dropping first means the flag survives
  // any failure above and checkAndExecPendingReset picks it up.
  lsClearAll()
  lsDeleteRaw(RESET_LS_KEY)
}

/**
 * Call once at boot. Synchronous localStorage check — zero cost on normal boots.
 * Safety net: if a reset was scheduled but the page reloaded before IDB could be
 * cleared (e.g. a crash mid-reset), clears IDB and reloads now.
 * Under normal operation, the wipe runs to completion before the reload, so this
 * returns immediately without touching IDB.
 */
export async function checkAndExecPendingReset(): Promise<void> {
  if (lsGetRaw(RESET_LS_KEY) !== '1') return
  // Deliberately do NOT clear the flag up front — the wipe's final step removes it
  // only once every database is actually gone, so a crash during this recovery still
  // leaves something to recover from on the next boot.
  try {
    await clearAllLocalStorage()
  } catch {
    // The recovery wipe itself failed. Drop the flag so a persistently failing wipe
    // cannot break startup on every launch — booting with stale data beats not
    // booting at all. Also prevents a reload loop.
    lsDeleteRaw(RESET_LS_KEY)
    return
  }
  window.location.reload()
}
