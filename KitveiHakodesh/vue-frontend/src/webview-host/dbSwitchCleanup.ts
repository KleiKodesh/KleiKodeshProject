/**
 * Forget everything keyed to the books of the old seforim DB.
 *
 * Book ids are assigned per database, so after the reader points the app at a
 * different library every stored id means something else — or nothing. The recent
 * books tiles are the visible symptom (a tile opens the wrong book, or fails), but
 * the same ids are also held by the last-read positions, the restored tabs, and the
 * search and TOC caches, so all of them are dropped together.
 *
 * A sequencer across IndexedDB, localStorage and the page lifecycle, like
 * `resetEverything` in features/settings/appResetState — and for the same reason it
 * lives beside the DB path it reacts to rather than inside any one store: no single
 * store owns book ids.
 *
 * What is deliberately KEPT: theme, fonts, shortcuts, workspaces, and everything
 * about local files and HebrewBooks. Switching library is not a request to lose them.
 * Settings are kept too, with one exception called out below — the hidden word-link
 * markers, the only setting that stores book ids.
 */
import {
  lsDelete,
  lsDeleteRaw,
  lsGetRaw,
  lsKeys,
  lsSetRaw,
  dropDatabase,
  sealDatabase,
} from '@/utils/persistence'

/**
 * Marks a switch as started, so a crash between the wipe and the reload is
 * recovered on the next boot. Unprefixed to match `__pendingReset`, whose pattern
 * this follows: both must be readable before anything else initialises.
 */
const DB_SWITCH_LS_KEY = '__pendingDbSwitch'

/**
 * Databases dropped whole, because every entry in them is book-scoped or is cheap to
 * rebuild from ordinary use. `app-recently-opened` is NOT here — it mixes seforim
 * books with local-file and HebrewBooks tiles the reader may have pinned, so it is
 * pruned by key instead (see below).
 *
 * `app-recent-tabs` does hold local-file locations too, but it is a rolling 50-entry
 * visit list with no pinning, rebuilt within minutes of normal reading; its own
 * loader already drops entries it cannot make sense of. Pruning it by key would also
 * need the active workspace's in-memory list cleared to stick, which the seal makes
 * unnecessary — so the whole-database drop is the simpler correct choice here.
 */
const STALE_DATABASES = [
  'app-lastread',
  'app-tabs',
  'app-recent-tabs',
  'app-search-cache',
  'app-catalog-toc-cache',
] as const

/**
 * localStorage keys holding book ids, or positions into results derived from them.
 *
 * `tabs:` — the open tab list per workspace; every /book-view tab carries a bookId.
 * `search.scroll:` — a scroll index into a search result set that no longer exists.
 * Both are matched by prefix because they are per-workspace / per-tab.
 */
const STALE_LS_PREFIXES = ['tabs:', 'search.scroll:'] as const

/**
 * The one setting that is book-id-keyed: the commentary book ids whose word-link
 * markers the reader hid. It is cleared with the rest, against the general rule that
 * settings survive — after a switch those ids address different commentaries, so
 * keeping it hides markers on books the reader never chose.
 */
const HIDDEN_WORD_LINK_MARKERS_KEY = 'text.hiddenWordLinkMarkers'

/**
 * Wipe the stale data. Call BEFORE reloading onto the new DB — the reload is what
 * makes the surviving stores refetch, and a store that reloads first would write
 * its stale in-memory copy back out.
 */
export async function clearStaleBookData(): Promise<void> {
  scheduleDbSwitchCleanup()
  // Imported lazily so a switch never forces the lazy-loaded store to initialise
  // earlier than it otherwise would.
  const { pruneSeforimBookEntries } = await import('@/stores/recentlyOpenedStore')
  // Sealed BEFORE the drops, not after: the stores owning this data hold in-memory
  // copies and re-persist them on any mutation, so a watcher firing mid-wipe would
  // recreate a database that had just been deleted. See sealDatabase.
  STALE_DATABASES.forEach(sealDatabase)
  await Promise.all([
    // Pruned, not dropped — keeps the reader's pinned local files and HebrewBooks.
    pruneSeforimBookEntries(),
    ...STALE_DATABASES.map((name) => dropDatabase(name)),
  ])
  // lsKeys returns app-namespace keys unprefixed, so they are removed with lsDelete
  // (which re-applies the namespace) rather than the raw variant used for the flag.
  for (const key of lsKeys()) {
    if (STALE_LS_PREFIXES.some((prefix) => key.startsWith(prefix))) lsDelete(key)
  }
  lsDelete(HIDDEN_WORD_LINK_MARKERS_KEY)
  // MUST come last, for the same reason as in appResetState: the flag is the only
  // record that a wipe was owed, so clearing it before the drops finish would mean
  // a crash mid-wipe leaves nothing for the next boot to pick up.
  lsDeleteRaw(DB_SWITCH_LS_KEY)
}

/** Arm the crash-safety net — synchronous localStorage write, zero IDB cost. */
function scheduleDbSwitchCleanup(): void {
  lsSetRaw(DB_SWITCH_LS_KEY, '1')
}

/**
 * Call once at boot, before any store reads its persisted state. Synchronous
 * localStorage check — zero cost on normal boots, because a completed switch has
 * already cleared the flag.
 *
 * Unlike the full reset's recovery this does NOT reload afterwards: it runs before
 * the stores initialise, so they read the already-cleaned state on this very boot.
 */
export async function checkAndExecPendingDbSwitchCleanup(): Promise<void> {
  if (lsGetRaw(DB_SWITCH_LS_KEY) !== '1') return
  try {
    await clearStaleBookData()
  } catch {
    // Drop the flag so a persistently failing wipe cannot stall every launch —
    // booting with stale recent books beats not booting at all. The seal still
    // holds for this session, so nothing stale that survived can be written back.
    lsDeleteRaw(DB_SWITCH_LS_KEY)
  }
}
