/**
 * Bridge to C# host for file operations.
 * All functions are no-ops / dev fallbacks when running outside the WebView2 host.
 *
 * In hosted mode, calls go via window.__webviewAction (injected by JsBridge.cs).
 * Push events from C# arrive via window.__onWebviewEvent (registered in db.ts).
 */

import { hasHostBridge, emitWebviewEvent } from './seforimDb'
import { serviceCall, serviceCallVoid } from './serviceClient'
import { ServiceUnavailableError, isPersistentReason } from './serviceAvailability'
import { decodeTextDetectEncoding } from '@/utils/textEncoding'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'

declare global {
  interface Window {
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
    __webviewShowPopOut?: boolean
    __onWebviewEvent?: ((msg: Record<string, unknown>) => void) | null
  }
}

/**
 * True when C# sets ShowPopOutButton = true on AppViewer (VSTO task-pane context).
 * Controls visibility of the "חלון עצמאי / חלונית" button in the hamburger menu.
 * Defaults to false in all other environments (standalone demo, browser dev).
 */
export const showPopOutButton = window.__webviewShowPopOut === true

/**
 * True when running inside the VSTO task-pane context (Word add-in).
 * Use this to conditionally hide features that don't make sense in the narrow
 * task-pane environment (e.g. split view).
 */
export const isVstoEnvironment = showPopOutButton

/**
 * True where a destination page must take over the current tab instead of opening
 * its own: the VSTO task pane (one tab visible, nowhere for a new one to go) and
 * the dev browser, which mirrors that behaviour so the task-pane path is what we
 * exercise day to day.
 */
export const navigatesDestinationsInPlace = isVstoEnvironment || !hasHostBridge

/**
 * True only when a native chrome tab strip (FluentChromeTabsForm) is actually
 * present to mirror the tabs — i.e. the standalone/demo WebView2 host, not the
 * VSTO task pane and not the dev browser. This is the exact condition under which
 * initTabMirror() wires up the strip, so UI that delegates to the native strip
 * (e.g. the Ctrl+T tab list) can gate on the same flag.
 *
 * The dev browser has no strip, so it must behave like VSTO (Vue-owned tab list) — which
 * hasHostBridge already excludes, since dev has no bridge channel.
 */
export const hasNativeChromeTabs = hasHostBridge && !isVstoEnvironment

function action<T>(name: string, args?: object): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  return window.__webviewAction(name, args) as Promise<T>
}

// ── Types ─────────────────────────────────────────────────────────────────────

export interface LocalFileResult {
  /** Ready-to-use URL served via virtual host */
  url: string
  fileName: string
  /** Absolute path on disk — persisted for session restore */
  filePath: string
}

export interface LocalFileRestoreResult {
  url: string
  /** Dev only: what the service actually serves for this file. Word-family docs render to
   *  'pdf' (Word conversion) or 'html' (Office-free fallback with wiki-style footnotes) —
   *  the caller must route /html-view for 'html'. Undefined in hosted mode (always PDF). */
  kind?: 'pdf' | 'html'
}

// ── Hosted actions ────────────────────────────────────────────────────────────

/**
 * The "open in a new tab" intent of an in-flight user-initiated file pick.
 * Set while pickLocalFile() awaits the hosted `pickFile` RPC, cleared afterwards.
 * localFileStore reads it so the push-event handlers can honour the frontend's
 * new-tab intent (the C# events don't carry it) and can tell a user pick apart
 * from an "Open With" launch (null → no pick in flight). See pendingPickOpenInNewTab().
 */
let _pendingPickOpenInNewTab: boolean | null = null

/** The in-flight user pick's new-tab intent, or null when no user pick is in flight. */
export function pendingPickOpenInNewTab(): boolean | null {
  return _pendingPickOpenInNewTab
}

/**
 * Open native file picker (PDF, Word, HTML formats).
 * For Word files, C# pushes a `localFileConversionStarted` event before replying,
 * so the tab can show a converting placeholder while waiting.
 *
 * In hosted mode, navigation is driven entirely by the C# push events (handled in
 * localFileStore, which targets the pane that initiated the pick). The returned
 * result is used only to finalize a cached Word conversion (which has no
 * localFileConversionReady push). Returns null if the user cancels.
 *
 * `initialDir` opens the dialog in that folder (used by the home page's frequent-folder
 * tiles). An empty string, or a folder that no longer exists, leaves the dialog at the
 * system default rather than failing the pick.
 */
export async function pickLocalFile(
  openInNewTab = false,
  initialDir = '',
): Promise<LocalFileResult | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: the SERVICE's native C# open-file dialog replaces the browser <input type=file>.
    // The browser picker only yields a blob (no absolute path), so picked files could never
    // persist across reloads; the native dialog returns the real PATH, which we then authorize
    // via openLocalFile (capability handle) exactly like a search-opened file — same-origin
    // /khs-file streaming, Word→PDF/HTML conversion, and reload persistence all apply.
    // Navigation reuses the store's hosted push-event handlers by replaying the same events
    // (emitWebviewEvent), so pane-targeting/placeholder/new-tab behavior stays identical.
    _pendingPickOpenInNewTab = openInNewTab
    try {
      const picked = await serviceCall<{ path?: string; fileName?: string; cancelled?: boolean }>(
        'pickLocalFile',
        { value: initialDir },
      )
      if (!picked?.path || picked.cancelled) return null
      const filePath = picked.path
      const fileName =
        picked.fileName || filePath.substring(Math.max(filePath.lastIndexOf('\\'), filePath.lastIndexOf('/')) + 1)
      const ext = fileName.substring(fileName.lastIndexOf('.')).toLowerCase()

      if (ext === '.txt') {
        // .txt opens in /txt-view straight off the path (TxtViewPage reads it via the bridge).
        emitWebviewEvent({ event: 'localFileTxtReady', fileName, filePath, openInNewTab })
        return { url: '', fileName, filePath }
      }

      const isDirect = ext === '.pdf' || ext === '.htm' || ext === '.html'
      // Word-family docs convert in the service (~4s cold Word) — show the converting
      // placeholder first, exactly as the hosted localFileConversionStarted push does.
      if (!isDirect) emitWebviewEvent({ event: 'localFileConversionStarted', fileName, filePath, openInNewTab })

      const r = await serviceCall<{
        handle?: string
        folderHandle?: string
        isOtzariaAddin?: boolean
        fileName?: string
        cancelled?: boolean
        error?: string
      }>('openLocalFile', { path: filePath })
      if (!r?.handle) {
        if (r?.cancelled) return null // user pressed ביטול — tab already reset, stay quiet
        console.error('[bridge] openLocalFile returned no handle', { filePath, reply: r })
        if (!isDirect)
          emitWebviewEvent({ event: 'localFileError', filePath, message: r?.error || t('webviewHost.fileOpenFailed') })
        return null
      }

      // The service reports what it will actually serve (docx → .pdf via Word, or .html via
      // the Office-free fallback) — the served name drives the viewer route.
      const servedName = r.fileName || fileName
      const servedExt = servedName.substring(servedName.lastIndexOf('.')).toLowerCase()
      const isHtmlFile = servedExt === '.html' || servedExt === '.htm'

      // HTML files: use the folder-scoped handle so sibling CSS/JS/images load correctly.
      // The URL is /khs-file/<folderHandle>/filename.html — the same "whole folder" model
      // that the hosted C# SetVirtualHostNameToFolderMapping already provides.
      const url = isHtmlFile && r.folderHandle
        ? `/khs-file/${r.folderHandle}/${encodeURIComponent(servedName)}`
        : `/khs-file/${r.handle}`

      if (isDirect) {
        emitWebviewEvent({
          event: 'localFileReady',
          url,
          fileName,
          filePath,
          openInNewTab,
          ...(isHtmlFile && r.isOtzariaAddin ? { isOtzariaAddin: true } : {}),
        })
        return { url, fileName, filePath }
      }
      // Converted: the caller finalizes the placeholder from this reply
      // (finalizeConvertingFromReply), mirroring the hosted cached-conversion path.
      return { url, fileName: servedName, filePath }
    } catch (err) {
      console.error('[bridge] pickLocalFile failed', err)
      return null
    } finally {
      _pendingPickOpenInNewTab = null
    }
  }
  _pendingPickOpenInNewTab = openInNewTab
  try {
    const res = await action<{
      cancelled?: boolean
      url?: string
      fileName?: string
      filePath?: string
      error?: string
    }>('pickFile', { initialDir })
    // An error reply and a cancel both come back as null, and the caller treats null as
    // "nothing to do" - so a genuine host-side failure looked exactly like the user
    // pressing Cancel, and the picker not opening had nothing to say for itself. Log it:
    // cancel stays silent, a failure names itself in the console.
    if (res.error) {
      console.error('[bridge] pickFile failed in the host: ' + res.error)
      return null
    }
    if (res.cancelled || !res.url) return null
    return { url: res.url, fileName: res.fileName!, filePath: res.filePath! }
  } finally {
    _pendingPickOpenInNewTab = null
  }
}

/**
 * Restore/open a local file tab from a file PATH.
 *
 * Hosted: C# re-registers the virtual host and returns the URL.
 * Dev: authorize the path with the KitveiHakodesh service — it validates the path and mints
 * an unguessable capability handle — then serve it through the same-origin `/khs-file` proxy,
 * which range-streams it so pdf.js loads the PDF progressively (never the whole file in
 * memory). Because we persist the PATH (not the URL) and rebuild this on every open, session
 * reload and recents work even though the service's port/token change each restart.
 */
export async function restoreLocalFile(filePath: string): Promise<LocalFileRestoreResult | null> {
  // Dev is detected by the ABSENCE of the C# bridge (isHosted is also true in dev, so it can't
  // distinguish — see pickLocalFile). In dev, authorize the path with the service and serve it
  // via the same-origin /khs-file proxy.
  if (typeof window.__webviewAction !== 'function') {
    try {
      const r = await serviceCall<{ handle: string; folderHandle?: string; fileName: string; error?: string }>(
        'openLocalFile',
        { path: filePath },
      )
      if (!r?.handle) {
        console.error('[bridge] restoreLocalFile: openLocalFile returned no handle', { filePath, reply: r })
        return null
      }
      // The service reports what it will actually serve: a converted Word doc comes back as
      // *.pdf (Word) or *.html (Office-free fallback) — the viewer route must follow suit.
      const servedExt = r.fileName?.toLowerCase().endsWith('.html') ? '.html' : '.pdf'
      const kind = servedExt === '.html' ? ('html' as const) : ('pdf' as const)
      // HTML files get a folder-scoped URL so siblings load; PDF gets single-file handle.
      // Encoded per segment: the host percent-DECODES the relative path (so a name with a
      // space or a non-ASCII character resolves), which means a literal '%' in a filename
      // has to arrive escaped or it decodes into a different name.
      const url = kind === 'html' && r.folderHandle
        ? `/khs-file/${r.folderHandle}/${encodeURIComponent(r.fileName ?? '')}`
        : `/khs-file/${r.handle}`
      return { url, kind }
    } catch (err) {
      console.error('[bridge] restoreLocalFile failed', { filePath }, err)
      return null
    }
  }
  const res = await action<{ url?: string; error?: string }>('restoreLocalFile', { filePath })
  if (res.error || !res.url) {
    console.error('[bridge] restoreLocalFile failed in the host', { filePath, reply: res })
    return null
  }
  return { url: res.url }
}

/**
 * Open a local file in the system's default program for its type (Word for .docx,
 * Acrobat/Reader for .pdf, the browser for .html, …) — the equivalent of double-clicking
 * the file in Explorer. Any file type is allowed (unlike the in-app viewer's allow-list):
 * the user is deliberately handing the file off to whatever program the OS associates with it.
 *
 * Hosted: the C# host shell-executes the path (`openInDefaultApp`).
 * Dev: the KitveiHakodesh service validates the path and shell-executes it on its machine
 * (`openFileInDefaultApp`) — the service runs on the same box as the dev browser.
 * Returns true when the launch was requested, false on any error/unavailable bridge.
 */
export async function openFileInDefaultApp(filePath: string): Promise<boolean> {
  if (typeof window.__webviewAction !== 'function') {
    try {
      const r = await serviceCall<{ ok?: boolean; error?: string }>('openFileInDefaultApp', {
        path: filePath,
      })
      return !!r?.ok
    } catch {
      return false
    }
  }
  try {
    const res = await action<{ ok?: boolean; error?: string }>('openInDefaultApp', { filePath })
    return !!res?.ok
  } catch {
    return false
  }
}

/**
 * PDF viewer TOC autosave: write an incremental-save `tail` back into the PDF at `offset`
 * (the length the viewer loaded). `anchor` = the loaded bytes just before `offset`; the
 * write is refused when they no longer match the disk (rules: Core PdfIncrementalSaveWriter).
 * Hosted: the C# host writes (`writePdfTail`, bytes as base64). Dev: the service op.
 * Returns an error message, or null when written.
 */
export async function writePdfTail(
  filePath: string,
  offset: number,
  tail: Uint8Array,
  anchor: Uint8Array,
): Promise<string | null> {
  try {
    const r =
      typeof window.__webviewAction !== 'function'
        ? // Copied into THIS realm: the viewer's arrays come from the PDF.js iframe, and a
          // cross-realm Uint8Array fails `instanceof` in the encoder and goes out as a map.
          await serviceCall<{ ok?: boolean; error?: string }>('writePdfTail', {
            path: filePath,
            offset,
            tail: new Uint8Array(tail),
            anchor: new Uint8Array(anchor),
          })
        : await action<{ ok?: boolean; error?: string }>('writePdfTail', {
            filePath,
            offset,
            tail: bytesToBase64(tail),
            anchor: bytesToBase64(anchor),
          })
    return r?.ok ? null : r?.error || 'write failed'
  } catch (e) {
    return String(e)
  }
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  }
  return btoa(binary)
}

/**
 * Open an http/https address in the user's default browser.
 *
 * Hosted: the C# host shell-executes the URL (`openUrlInSystemBrowser`) - a plain
 * window.open inside WebView2 would open a WebView popup, not the system browser.
 * Dev: the page already runs in the system browser, so a new tab is the same thing.
 */
export async function openUrlInSystemBrowser(url: string): Promise<boolean> {
  if (!/^https?:\/\//i.test(url)) return false
  if (typeof window.__webviewAction !== 'function') {
    window.open(url, '_blank', 'noopener,noreferrer')
    return true
  }
  try {
    const res = await action<{ ok?: boolean; error?: string }>('openUrlInSystemBrowser', { url })
    return !!res?.ok
  } catch {
    return false
  }
}

/**
 * Read a .txt file from disk and return its content (string).
 * Used by TxtViewPage to load content on mount and on session restore.
 */
export async function readTxtFileContent(filePath: string): Promise<string | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: authorize + fetch the .txt through the same capability-gated /khs-file proxy, then
    // decode with the shared encoding-detection util (BOM → UTF-8 → Windows-1255 fallback), the
    // same detection the hosted lib uses — a naive UTF-8 decode garbles legacy Hebrew .txt.
    try {
      const r = await serviceCall<{ handle: string; error?: string }>('openLocalFile', { path: filePath })
      if (!r?.handle) return null
      const res = await fetch(`/khs-file/${r.handle}`, { cache: 'no-store' })
      if (!res.ok) return null
      return decodeTextDetectEncoding(await res.arrayBuffer())
    } catch {
      return null
    }
  }
  const res = await action<{ textContent?: string; error?: string }>('readTxtFileContent', { filePath })
  if (res.error || !res.textContent) return null
  return res.textContent
}

/**
 * A served PDF result from the service (`triggerHbDownload` / `restoreHbPdf`): the URL the
 * viewer can load. Dev loads it through the same-origin `/khs-file` proxy by capability
 * handle; the hosted page has no HTTP host, so the C# host serves the file's PATH from a
 * WebView2 virtual host instead (the same `restoreLocalFile` action every local file uses).
 */
async function servedPdfUrl(r: { handle?: string; path?: string }): Promise<string | null> {
  if (!r.handle) return null
  if (typeof window.__webviewAction !== 'function') return `/khs-file/${r.handle}`
  if (!r.path) return null
  const hosted = await action<{ url?: string; error?: string }>('restoreLocalFile', { filePath: r.path })
  if (!hosted?.url) console.error('[hb-download] host could not serve the PDF', { path: r.path, reply: hosted })
  return hosted?.url || null
}

/**
 * Restore a HebrewBooks PDF tab from a persisted book ID.
 * The service checks the local folder first, then its cache (no download). A miss returns
 * redownload — and, like the download flow, the re-download is kicked off here and finishes
 * through the hbPdfReady / hbPdfCancelled events so the store's converting tab is resolved.
 * Returns null on failure.
 */
export async function restoreHbPdf(
  bookId: string,
  bookTitle: string,
  tabId: string,
  localFolder?: string,
): Promise<{ url: string } | { redownload: true } | null> {
  try {
    const r = await serviceCall<{ handle?: string; path?: string; redownload?: boolean; error?: string }>(
      'restoreHbPdf',
      { bookId, localFolder: localFolder || '' },
    )
    const url = await servedPdfUrl(r)
    if (url) return { url }
    if (r?.redownload) {
      triggerHbDownload(bookId, bookTitle, '', tabId, localFolder, navigator.onLine)
      return { redownload: true }
    }
    console.error('[hb-download] restoreHbPdf: no PDF and no redownload', { bookId, reply: r })
    return null
  } catch (err) {
    console.error('[hb-download] restoreHbPdf failed', { bookId }, err)
    return null
  }
}

/**
 * Notify C# that a local file tab was closed so it can decrement the virtual host ref count.
 * Only relevant for local files (not cache-based files).
 */
export function disposeLocalFileHost(filePath: string): void {
  if (!hasHostBridge || !filePath) return
  action('disposeLocalFileHost', { filePath }).catch(() => {})
}

/**
 * Toggle the host user control visibility — pops the viewer out into a floating window
 * or returns it to the VSTO task pane / host form.
 */
export function togglePopOut(): void {
  if (!hasHostBridge) return
  action('TogglePopOut').catch(() => {})
}

/** Ceiling for a single hosted reset step. Generous: the FTS wipe legitimately blocks
 *  until an in-flight index merge drains, which is slow but finite. */
const RESET_STEP_TIMEOUT_MS = 30_000

/**
 * Full app reset — deletes the FTS index, resets C# settings, then reloads.
 * Called by resetEverything() in features/settings/appResetState.ts, which wipes
 * the local databases first — do not call this directly.
 */
export async function resetHostApp(): Promise<void> {
  // The catalog TOC index is reset the same way in both modes now that the hosted app has its
  // own handler for it. It used to be an unguarded serviceCall sitting above this branch, on
  // the claim that "serviceCall reaches the service in both modes" — it does not: serviceClient
  // discovers the service through the DEV SERVER's /khs-endpoint route, which does not exist in
  // the hosted app. Discovery then retries the full 82 attempts before giving up (backoff caps
  // at 1s, so ~77s), and postRpc re-enters discovery on each of its own 6 attempts — minutes,
  // not seconds. As the first awaited step here, that one line stalled the entire reset and
  // then failed, with nothing shown — the "reset does nothing" report.
  await withTimeout(resetCatalogTocIndex(), RESET_STEP_TIMEOUT_MS)

  if (typeof window.__webviewAction !== 'function') {
    // Dev: the C# host doesn't exist, so reset the indexes through the service, then reload.
    // Browser storage is NOT touched here — resetEverything() has already wiped every database
    // and the whole kitvei-hakodesh.* localStorage namespace before calling this. A raw
    // localStorage.clear() used to sit here, which both duplicated that work and reached
    // outside the app's namespace.
    await serviceCall('ftsResetIndex').catch(() => {})
    await serviceCall('resetDocumentLocatorIndex').catch(() => {})
    // Forget the saved database path, exactly as the hosted 'resetSettings' step below does
    // by deleting the whole settings subtree. Without this, dev kept the registry value and
    // the resolver's first step (saved choice, file still present) short-circuited before it
    // could probe — so a reset on a machine with both libraries installed stayed on whichever
    // one had been chosen instead of returning to the Otzaria-first default.
    // Warn rather than swallow silently. The two calls above fail visibly — a stale index
    // rebuilds itself — but this one failing leaves the saved path in place permanently and
    // the reset looks like it worked, which is the exact bug this call was added to fix.
    await serviceCall('clearSeforimDbPath').catch((e) => {
      console.warn('[bridge] reset could not clear the saved database path:', e)
    })
    // Hosted 'resetSettings' below deletes the WHOLE KitveiHakodesh subtree, so dev has to
    // clear the other shared settings by hand or the same button leaves different state
    // behind depending on the mode. This is the only one dev can observe and act on; the rest
    // of the subtree (window geometry, title-bar colours) is hosted-only and meaningless here,
    // and HebrewBooks/CsvLastUpdated has no dev-reachable clear — it only re-downloads a CSV.
    await clearHbLocalFolder().catch(() => {})
    window.location.reload()
    return
  }

  // Each hosted step replies as soon as its work is QUEUED, not when it completes — the wipes
  // have to drain in-flight index builds first, which takes tens of seconds on a large index
  // and is exactly what made the reset look frozen. The timeouts are therefore a safety net
  // against a lost reply, not the normal path.
  //
  // The three wipes run concurrently with each other and with the reloaded page's startup;
  // only DeleteFtsIndex has an ordering guarantee, and it is the one that needs it. Its
  // handler posts to a single-consumer actor that the reloaded page's OnDbReady also posts
  // to, so the rebuild cannot overtake the wipe. The other two each own their own index
  // directory and gate readers behind their own readiness check, so nothing they race with
  // can observe a half-wiped state. Do not add a fourth step here assuming a shared queue.
  // The service owns every index and data setting in both runtimes; only the host-side
  // window and theme settings are still the WebView2 host's to reset.
  await withTimeout(serviceCall('ftsResetIndex'), RESET_STEP_TIMEOUT_MS)
  await withTimeout(serviceCall('resetDocumentLocatorIndex'), RESET_STEP_TIMEOUT_MS)
  await withTimeout(serviceCall('clearSeforimDbPath'), RESET_STEP_TIMEOUT_MS)
  await withTimeout(clearHbLocalFolder(), RESET_STEP_TIMEOUT_MS)
  await withTimeout(action('resetSettings'), RESET_STEP_TIMEOUT_MS)
  // clearBrowsingData is set ONLY here. The same action is sent by seforimDb.onDbReady after
  // a DB-path change, where clearing would wipe tabs and last-read positions the user never
  // asked to lose. Safe on this path: resetEverything() has already dropped that storage.
  action('reload', { clearBrowsingData: true }).catch(() => window.location.reload())
}

/**
 * Resolve when `p` settles or the timeout elapses, whichever comes first — never rejects.
 *
 * Every step of the reset is best-effort by design: the reload at the end is what the user
 * actually needs, and no individual failure should be able to prevent it.
 */
function withTimeout(p: Promise<unknown>, ms: number): Promise<void> {
  return new Promise<void>((resolve) => {
    const timer = setTimeout(resolve, ms)
    p.then(
      () => {
        clearTimeout(timer)
        resolve()
      },
      () => {
        clearTimeout(timer)
        resolve()
      },
    )
  })
}

/** One catalog TOC hit as it comes off the wire, from either transport. */
export type CatalogTocHit = {
  bookId: number
  /** -1 = no resolved line. */
  lineIndex: number
  /** " / "-joined TOC segments WITHOUT the book title (prefixed client-side); "" for a title hit. */
  tocPath: string
  /** 0 = book-title hit, 1+ = TOC depth. */
  level: number
  treeOrder: number
}

export type CatalogTocSearchResult = {
  /** False while no index is available yet (first build) — the caller retries. */
  ready: boolean
  results: CatalogTocHit[]
  /** True when a newer search superseded this one — discard and retry. */
  superseded?: boolean
  error?: string | null
  /** With ready = false: the seforim DB is missing — the index can never become ready. */
  dbMissing?: boolean
  /** Build progress while not ready. */
  builtBooks?: number
  totalBooks?: number
  /** With ready = false: the last build failed and none is running — stop retrying. */
  buildFailure?: string | null
  /** The last build failed and an automatic retry is running now. */
  retryingAfterBuildFailure?: boolean
  /** Not built yet; a timed retry is pending (DB mid-update, index locked). Transient. */
  waiting?: boolean
}

/** Where the catalog TOC index stands (catalogTocStatus wire shape, camelCased). */
export type CatalogTocIndexStatus = {
  ready: boolean
  indexing: boolean
  builtBooks: number
  totalBooks: number
  dbMissing: boolean
  buildFailure?: string | null
  retryingAfterBuildFailure?: boolean
  waiting?: boolean
}

/** Catalog TOC index status. Polled only while a build is known to run. */
export function catalogTocIndexStatus(): Promise<CatalogTocIndexStatus | null> {
  return serviceCall<CatalogTocIndexStatus>('catalogTocStatus').catch(() => null)
}

/**
 * Search the catalog TOC (Lucene) index — book titles and full TOC paths in one query.
 * The KitveiHakodesh service owns the index in both runtimes; serviceClient delivers
 * camelCase keys, so the result needs no per-mode normalisation.
 */
export async function catalogTocSearch(query: string): Promise<CatalogTocSearchResult> {
  return serviceCall<CatalogTocSearchResult>('catalogTocSearch', { query })
}

/** Result of a book-title search (catalogTitleSearch wire shape, camelCased). The not-ready
 *  fields mean what they mean on CatalogTocSearchResult. */
export type CatalogTitleSearchResult = {
  ready: boolean
  /** Matching book ids, best first. Uncapped. */
  bookIds: number[]
  /** The index was reset under this search — discard and retry. */
  superseded?: boolean
  error?: string | null
  dbMissing?: boolean
  builtBooks?: number
  totalBooks?: number
  buildFailure?: string | null
  retryingAfterBuildFailure?: boolean
  waiting?: boolean
}

/**
 * Search book titles (with catalog path and authors) in the catalog index — level-0 docs
 * only, the last word also matched as a prefix. Same index and transport as catalogTocSearch.
 */
export async function catalogTitleSearch(query: string): Promise<CatalogTitleSearchResult> {
  return serviceCall<CatalogTitleSearchResult>('catalogTitleSearch', { query })
}

/**
 * Wipe the catalog TOC (Lucene) index so it is rebuilt from the seforim DB.
 *
 * Normally self-healing — the index carries a DB content stamp and rebuilds whenever the
 * seforim DB or the index format version changes — so this exists for the case a stamp
 * cannot catch: an index corrupted in place.
 */
export async function resetCatalogTocIndex(): Promise<void> {
  await serviceCall('catalogTocResetIndex').catch(() => {})
}

/**
 * Trigger a full DocumentLocator index rebuild on the C# side.
 * The service wipes its Lucene index and re-crawls the NTFS MFT from scratch.
 * Returns as soon as the rebuild has been kicked off — the rebuild itself runs in the
 * background and pushes no events. While it runs, searches wait for the new index and
 * localFileIndexStatus() reports that a build is in progress.
 */
export async function resetDocumentLocatorIndex(): Promise<void> {
  await serviceCall('resetDocumentLocatorIndex').catch(() => {})
}

/**
 * Read/write the excluded folders list. The service persists it to excluded_folders.json
 * beside the file-search index and applies it at search time, so a change takes effect
 * immediately with no reindex. One Vue dialog (SettingsExcludedFoldersDialog) in both runtimes.
 */
export async function getExcludedFolders(): Promise<string[]> {
  try {
    const result = await serviceCall<{ folders?: string[] }>('getExcludedFolders')
    return result?.folders ?? []
  } catch {
    return []
  }
}

/** Persist the full replacement list. Returns the list as it stands on disk afterwards. */
export async function setExcludedFolders(folders: string[]): Promise<string[]> {
  const result = await serviceCall<{ folders?: string[]; error?: string }>('setExcludedFolders', {
    folders,
  })
  if (result?.error) throw new Error(result.error)
  return result?.folders ?? []
}

/**
 * Wipe the FTS index so the service rebuilds it from the seforim DB.
 */
export async function resetSearchIndex(): Promise<void> {
  await serviceCall('ftsResetIndex').catch(() => {})
}

/**
 * Collect environment diagnostics from the C# host.
 * Returns a flat key/value map with process bitness, OS bitness, Office bitness,
 * SQLite.Interop.dll presence/bitness, and assembly paths.
 * Used to diagnose the 0x8007000B SQLite bitness mismatch error.
 */
export async function getDiagnostics(): Promise<Record<string, string> | null> {
  if (typeof window.__webviewAction !== 'function') return null
  try {
    const res = await action<{ diagnostics?: Record<string, string>; error?: string }>(
      'getDiagnostics',
    )
    return res.diagnostics ?? null
  } catch {
    return null
  }
}

/**
 * Toggle fullscreen mode on the host window.
 * Sets FormBorderStyle.None and WindowState.Maximized when entering fullscreen,
 * restores normal state when exiting.
 */
export async function toggleFullscreen(): Promise<void> {
  if (!hasHostBridge) return
  await action('toggleFullscreen').catch(() => {})
}

/**
 * Search the Hebrew Books catalog via the C# database backend.
 * If localFolder is provided, C# stamps hasLocalFile on each result.
 * Returns books as plain objects — caller casts to HebrewBook[].
 */
export function hbSearch(
  query: string,
  localFolder?: string,
  limit?: number,
): Promise<{ books?: unknown[]; error?: string }> {
  const args = { query, localFolder: localFolder || '', ...(limit !== undefined ? { limit } : {}) }
  return serviceCall<{ books: unknown[] }>('hbSearch', args).catch((err) => ({
    error: err instanceof Error ? err.message : 'Search error',
  }))
}

/**
 * Notify C# to warm up the DocumentLocator service in the background.
 * Fire-and-forget — no reply is needed. By the time the user types a query
 * the service will likely already be running and the index ready.
 */
export function fileSystemSearchWarmup(): void {
  serviceCallVoid('locateDocumentsWarmup')
}

export interface FileSystemSearchHit {
  fileName: string
  path: string
  modifiedDate?: number
  /** Non-empty only for Otzaria addin entry points. Value is "תוסף אוצריא: {name}". */
  addinName?: string
}

/**
 * Search the file system using the DocumentLocator service.
 * Starts the service on demand if it has stopped and waits until the index is ready before returning results.
 * Vue's loading animation covers the wait. Returns up to `max` results (default 200).
 */
export function fileSystemSearch(
  query: string,
  max = 200,
): Promise<{
  results?: FileSystemSearchHit[]
  total?: number
  error?: string
}> {
  // The service owns the file index (DocumentLocator in-process) and the result shaping.
  return serviceCall<{
    results: FileSystemSearchHit[]
    total: number
  }>('locateDocuments', { query, max }).catch((err) => ({
    error: err instanceof Error ? err.message : 'Search error',
  }))
}

/**
 * Whether the file index is currently being built.
 *
 * A search waits for the index, and a first-time crawl can take minutes — long enough
 * that a spinner saying "searching" reads as a hang. The page polls this while a search
 * is outstanding so it can say "building the index" instead.
 *
 * Reports the state as of right now and never prompts. It is not entirely passive on the
 * hosted side — it may start the service — so only poll it while a search is already
 * waiting. Any failure is reported as not-building, because the search call itself is
 * what surfaces real errors.
 */
export type LocalFileIndexStatus = {
  isBuilding: boolean
  message?: string
  /** Why the last build failed, when none is running. */
  buildFailure?: string | null
  /** Estimated build progress 0-100 (Core estimates it); -1 when unknown. */
  progressPercent?: number
  /** Set only by this wrapper: the call itself failed, so nothing is known. */
  unreachable?: boolean
}

export function localFileIndexStatus(): Promise<LocalFileIndexStatus> {
  return serviceCall<LocalFileIndexStatus>('localFileIndexStatus').catch(
    () => ({ isBuilding: false, unreachable: true }),
  )
}

/**
 * Export book content as HTML to a new Word document. Word opens (or a running instance is
 * reused) with the HTML as the document content.
 *
 * Hosted: the WebView2 host drives Word through the Office PIA (WordExporter.ExportCore).
 * Dev: the KitveiHakodesh service drives Word over raw COM/IDispatch instead (the PIA and
 * `dynamic` are unavailable under native AOT) — see AotWordConverter.ExportHtml.
 *
 * NOTE: guard on __webviewAction, NOT isHosted — isHosted is TRUE in dev while the bridge
 * channel is absent, so an isHosted check would send dev down the hosted path and reject.
 */
export async function exportToWord(html: string, title: string = ''): Promise<{ ok?: boolean; error?: string }> {
  if (typeof window.__webviewAction !== 'function') {
    try {
      const r = await serviceCall<{ ok?: boolean; error?: string }>('exportToWord', { html, title })
      return { ok: !!r?.ok, error: r?.error }
    } catch (e) {
      return { error: e instanceof Error ? e.message : String(e) }
    }
  }
  // Same try/catch as pasteIntoWord: the hosted handler now awaits the real Word result
  // instead of replying ok=true immediately, so this can reject where it previously always
  // resolved. Callers get an { error } to inspect rather than an unhandled rejection.
  try {
    return await action<{ ok?: boolean; error?: string }>('exportToWord', { html, title })
  } catch (e) {
    return { error: e instanceof Error ? e.message : String(e) }
  }
}

/**
 * Paste into Word at the current cursor position by reading from the Windows clipboard.
 * The caller must have already set the clipboard via execCopyHtml / copyToClipboard
 * before calling this — the C# side pastes whatever is on the clipboard, so no HTML
 * needs to be sent over the bridge.
 *
 * Hosted: the WebView2 host drives Word through the Office PIA (WordExporter).
 * Dev: the KitveiHakodesh service drives Word over raw COM/IDispatch instead (the PIA
 * and `dynamic` are both unavailable under native AOT) — see AotWordConverter.PasteAtCursor.
 * The service runs on the same machine as the dev browser and shares its clipboard, so
 * the already-copied HTML is exactly what gets pasted.
 *
 * NOTE: guard on __webviewAction, NOT isHosted — isHosted is TRUE in dev while the
 * bridge channel is absent, so an isHosted check would send dev down the hosted path
 * and reject with 'bridge not available'.
 */
export async function pasteIntoWord(): Promise<{ ok?: boolean; error?: string }> {
  const result = await runPasteIntoWord()
  // Reported HERE rather than at the call sites: all five of them fire this as
  // `pasteIntoWord().catch(() => {})` from inside a copy event, where there is no
  // sensible place to await an answer. Surfacing the failure centrally is what turns
  // "the button does nothing" into a message naming the reason — the C# side now
  // returns one instead of swallowing it into a Release-mode no-op.
  if (result.error) showToast(result.error, { variant: 'error' })
  return result
}

async function runPasteIntoWord(): Promise<{ ok?: boolean; error?: string }> {
  if (typeof window.__webviewAction !== 'function') {
    try {
      const r = await serviceCall<{ ok?: boolean; error?: string }>('pasteIntoWord', {})
      return { ok: !!r?.ok, error: r?.error }
    } catch (e) {
      return { error: e instanceof Error ? e.message : String(e) }
    }
  }
  try {
    return await action<{ ok?: boolean; error?: string }>('pasteIntoWord', {})
  } catch (e) {
    return { error: e instanceof Error ? e.message : String(e) }
  }
}

/**
 * Place a PNG image on the Windows clipboard via the C# host (hosted only).
 *
 * Needed because the host serves the app from http://kitveihakodesh-app/ — plain http on a
 * non-localhost hostname, which Chromium treats as an INSECURE context, so navigator.clipboard
 * is not exposed there and C# must do it with System.Windows.Forms.Clipboard instead.
 *
 * There is deliberately no dev equivalent: dev is served from http://localhost, a secure context
 * where navigator.clipboard.write() works, so callers use the browser API directly rather than
 * calling this and having it reject.
 *
 * `dataUrl` must be a PNG data: URL (canvas.toDataURL('image/png')).
 */
export function copyImageToClipboard(dataUrl: string): Promise<{ ok?: boolean; error?: string }> {
  return action<{ ok?: boolean; error?: string }>('copyImageToClipboard', { dataUrl })
}

/**
 * Notify the C# host that the Vue theme has changed.
 * The host persists the preference and updates the WinForms title bar via DarkNet.
 * chromeColor is the theme's title-bar background (hex) — the native chrome tab
 * strip derives its full palette from it so it matches the Vue theme exactly.
 * accentColor drives the active-tab indicator in the native tab-list dropdown.
 * uiLanguage ('he' | 'en') switches the native chrome's direction (RTL/LTR) and texts.
 * Fire-and-forget — no meaningful return value.
 */
export function setTheme(
  isDark: boolean,
  chromeColor?: string,
  accentColor?: string,
  borderColor?: string,
  uiLanguage?: string,
): void {
  if (typeof window.__webviewAction !== 'function') return
  action('setTheme', {
    isDark,
    ...(uiLanguage ? { uiLanguage } : {}),
    ...(chromeColor ? { chromeColor } : {}),
    ...(accentColor ? { accentColor } : {}),
    ...(borderColor ? { borderColor } : {}),
  }).catch(() => {})
}

// ── Native chrome tabs mirror ────────────────────────────────────────────────

export interface MirroredTab {
  id: string
  title: string
  /** Full breadcrumb ("title · toc path") for the native tab-list dropdown; equals title when no path. */
  listTitle: string
  pane: 1 | 2
  /** Which favicon to draw — a key into the set sent by notifyTabIcons. */
  iconKey: string
}

/** Full snapshot of the Vue tab store for the native chrome tab strip. */
export interface TabsSnapshot {
  tabs: MirroredTab[]
  /** Pane 1's active tab id. */
  activeTabId: string
  /** Pane 2's active tab id; '' when split view is off. */
  pane2ActiveTabId: string
  /** Whether split view is open — splits the native strip into two regions. */
  splitView: boolean
  /** Which pane has focus (1 when split view is off). */
  focusedPane: 1 | 2
  /** Pane 2's share of the window width (splitViewFraction) — drag baseline / fallback. */
  splitFraction: number
  /** Rendered split divider's device-pixel bounds from the viewport left, for exact alignment; -1/0 when unmeasured. */
  splitDividerLeftPx: number
  splitDividerWidthPx: number
}

/**
 * Push a full snapshot of the Vue tab store to the C# host so the native chrome
 * tab strip can mirror it (membership, titles, per-pane active tabs, split state).
 * Fire-and-forget.
 */
export function notifyTabsChanged(snapshot: TabsSnapshot): void {
  if (typeof window.__webviewAction !== 'function') return
  action('tabsChanged', { ...snapshot }).catch(() => {})
}

/**
 * Ask the C# host to toggle the native tab strip's tab-list dropdown (Ctrl+T).
 * The strip owns that popup, so this is the only way to open it from the page.
 *
 * Works even when the strip is hidden (fullscreen, Ctrl+H): the mirror anchors the
 * popup to the tab-list button's position, which the strip keeps computing whether
 * or not it is painted. Fire-and-forget; a no-op wherever there is no native strip.
 *
 * `quickSwitchStep` turns it into the Ctrl+Tab switcher: pass +1 (next) or -1
 * (previous) and the popup opens in hold-to-scroll mode, staying up until Ctrl is
 * released and only then activating the highlighted tab. The native popup takes OS
 * focus the moment it shows, so the page never sees the matching keyup — the whole
 * hold-and-release gesture is owned by the C# side, and this call is one-way.
 */
export function toggleChromeTabList(quickSwitchStep = 0): void {
  if (typeof window.__webviewAction !== 'function') return
  action('toggleChromeTabList', { quickSwitchStep }).catch(() => {})
}

/**
 * Push the rasterized favicon set to the C# host, keyed by icon name. Sent once at
 * startup and again whenever the device pixel ratio changes, so the bitmaps match
 * the size the strip draws them at. Tab snapshots reference these by `iconKey`,
 * which keeps the per-snapshot payload unchanged. Fire-and-forget.
 */
export function notifyTabIcons(icons: { key: string; png: string }[]): void {
  if (typeof window.__webviewAction !== 'function') return
  action('tabIcons', { icons }).catch(() => {})
}

/** How fetching a HebrewBooks PDF ended - `url` when it can be shown, else the reason. */
export type HbPdfFetchResult =
  | { url: string; cancelled?: undefined; notFound?: undefined; noInternet?: undefined; error?: undefined }
  | { url?: undefined; cancelled: true; notFound?: undefined; noInternet?: undefined; error?: undefined }
  | { url?: undefined; cancelled?: undefined; notFound: boolean; noInternet: boolean; error?: string }

/**
 * Fetches a HebrewBooks PDF (local folder, then cache, then download) and resolves to a URL
 * the PDF.js viewer can load - ONE implementation shared by the tab download flow
 * (`triggerHbDownload`, which turns the result into hbPdfReady / hbPdfCancelled events) and
 * callers that show the PDF without a tab, like the book view's side-by-side edition.
 */
export async function fetchHbPdfUrl(
  bookId: string,
  localFolder?: string,
  isOnline?: boolean,
): Promise<HbPdfFetchResult> {
  try {
    const r = await serviceCall<{
      handle?: string
      path?: string
      notFound?: boolean
      noInternet?: boolean
      cancelled?: boolean
      error?: string
      detail?: string
    }>('triggerHbDownload', { bookId, localFolder: localFolder || '', isOnline: isOnline !== false })
    const url = await servedPdfUrl(r)
    if (url) return { url }
    // The user aborted the download; nothing to show.
    if (r?.cancelled) return { cancelled: true }
    console.error(`[hb-download] book ${bookId} failed:
${r?.detail || r?.error || JSON.stringify(r)}`)
    // Anything else carries the service's reason (HTTP status, disk error with the folder,
    // invalid id) so the caller can show it instead of failing silently.
    return { notFound: !!r?.notFound, noInternet: !!r?.noInternet, error: r?.error || undefined }
  } catch (e) {
    console.error(`[hb-download] book ${bookId} threw:`, e)
    const noInternet = !navigator.onLine
    return {
      notFound: false,
      noInternet,
      error: noInternet ? undefined : e instanceof Error ? e.message : String(e),
    }
  }
}

/**
 * Trigger a HebrewBooks PDF download, then open it. The service checks {localFolder}\{bookId}.pdf
 * and its cache first, otherwise downloads the PDF ENTIRELY in C# (HttpClient) and answers
 * once it is on disk. The store's tab is finished through the hbPdfReady / hbPdfCancelled
 * events (emitWebviewEvent), so the listeners in localFileStore run the same way whatever
 * started the download. The `url` argument is unused: the service builds its own URL from
 * the book id.
 */
export function triggerHbDownload(
  bookId: string,
  bookTitle: string,
  _url: string,
  tabId: string,
  localFolder?: string,
  isOnline?: boolean,
): Promise<{ ok?: boolean }> {
  ;(async () => {
    const result = await fetchHbPdfUrl(bookId, localFolder, isOnline)
    if (result.url) {
      emitWebviewEvent({ event: 'hbPdfReady', url: result.url, bookId, bookTitle, tabId })
    } else if (result.cancelled) {
      // The tab was already reset, nothing to show.
      emitWebviewEvent({ event: 'hbPdfCancelled', tabId, cancelled: true })
    } else {
      emitWebviewEvent({
        event: 'hbPdfCancelled',
        tabId,
        notFound: result.notFound,
        noInternet: result.noInternet,
        error: result.error,
      })
    }
  })()
  return Promise.resolve({ ok: true })
}

/**
 * Poll the live byte progress of an in-flight HebrewBooks download. Returns { active, received,
 * total } where total 0 means the server sent no Content-Length (show MB, not %), or null when
 * nothing is downloading. Both modes stream the PDF themselves — the service in dev, C# over
 * HttpClient when hosted — so both can report real bytes.
 */
export async function getHbDownloadProgress(
  bookId: string,
): Promise<{ active: boolean; received: number; total: number } | null> {
  if (!bookId) return null
  try {
    const r = await serviceCall<{ active?: boolean; received?: number; total?: number }>(
      'hbDownloadProgress',
      { bookId },
    )
    if (!r) return null
    return { active: !!r.active, received: r.received || 0, total: r.total || 0 }
  } catch {
    return null
  }
}

/**
 * Abort an in-flight HebrewBooks download (the ביטול button) — trips the per-book cancellation
 * so the streamed copy stops and its partial file is cleaned up, in both modes. Fire-and-forget.
 */
export function cancelHbDownload(bookId: string): void {
  if (!bookId) return
  serviceCallVoid('cancelHbDownload', { bookId })
}

/**
 * Abort an in-flight Word/document conversion (the ביטול button). Dev only — trips the service's
 * per-source cancellation so it discards the result and deletes the partial cache file (Word
 * self-quits, no orphan). Fire-and-forget. Hosted mode has its own conversion-cancel path.
 */
export function cancelLocalFileConversion(sourcePath: string): void {
  if (typeof window.__webviewAction === 'function' || !sourcePath) return
  serviceCallVoid('cancelConversion', { path: sourcePath })
}

/**
 * Check which of the supplied book IDs have a {bookId}.pdf in the local folder.
 * Returns the subset of IDs that exist on disk. Batch call — send all visible IDs at once.
 */
export function checkHbLocalFiles(
  bookIds: string[],
  localFolder: string,
): Promise<{ existingIds?: string[]; error?: string }> {
  return serviceCall<{ existingIds?: string[]; error?: string }>('checkHbLocalFiles', {
    bookIds,
    localFolder,
  }).catch(() => ({ existingIds: [] }))
}

type HbDeleteLocalResult = { ok?: boolean; notFound?: boolean; noRecycleBin?: boolean; error?: string }

/**
 * Delete a HebrewBooks PDF from the user's configured local folder - permanently, or into
 * the Recycle Bin when `toRecycleBin` is set.
 * Returns { ok: true } on success, { notFound: true } if the file is not there,
 * { noRecycleBin: true } if the drive has no Recycle Bin (file left in place),
 * or { error: "..." } on failure.
 *
 * The Recycle Bin move goes through the user-side host when there is one: the installed
 * service runs as LocalSystem and would fill SYSTEM's bin. Dev has no host bridge, but its
 * service runs as the user, so the service path is right there.
 */
export function deleteHbLocalFile(
  bookId: string,
  localFolder: string,
  toRecycleBin = false,
): Promise<HbDeleteLocalResult> {
  if (toRecycleBin && typeof window.__webviewAction === 'function')
    return action<HbDeleteLocalResult>('recycleHbLocalFile', { bookId, localFolder })
  return serviceCall<HbDeleteLocalResult>('deleteHbLocalFile', {
    bookId,
    localFolder,
    toRecycleBin,
  })
}

/**
 * Open Windows Explorer with the book's local PDF file selected and highlighted.
 * Equivalent to VS Code's "Reveal in File Explorer" — explorer.exe /select,"path".
 * Returns { ok: true } on success, { notFound: true } if the file is missing,
 * or { error: "..." } on failure.
 */
export function revealHbLocalFile(
  bookId: string,
  localFolder: string,
): Promise<{ ok?: boolean; notFound?: boolean; error?: string }> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: no WinForms "reveal in Explorer" path. Hand the file to the OS default program via
    // the service's openFileInDefaultApp (same op the "open in default program" button uses).
    const sep = localFolder.endsWith('\\') || localFolder.endsWith('/') ? '' : '\\'
    return serviceCall<{ ok?: boolean; error?: string }>('openFileInDefaultApp', {
      path: `${localFolder}${sep}${bookId}.pdf`,
    }).catch(() => ({ error: 'failed' }))
  }
  return action<{ ok?: boolean; notFound?: boolean; error?: string }>('revealHbLocalFile', {
    bookId,
    localFolder,
  })
}

/**
 * Get/set the HebrewBooks local download folder. The service persists it in the SHARED
 * registry value (HKCU\...\KitveiHakodesh\HebrewBooks\LocalFolder) for both runtimes.
 */
export async function getHbLocalFolderFromRegistry(): Promise<string> {
  try {
    const r = await serviceCall<{ value?: string }>('getHbLocalFolder')
    return r?.value || ''
  } catch {
    return ''
  }
}

export function setHbLocalFolderInRegistry(path: string): void {
  serviceCallVoid('setHbLocalFolder', { value: path })
}

/**
 * Download a book through the native Save As dialog. Resolves only when the save ends -
 * saved, cancelled, failed or book not found - so the caller can show progress meanwhile.
 */
export async function triggerHbSaveAs(
  bookId: string,
  bookTitle: string,
  url: string,
): Promise<{ ok?: boolean; cancelled?: boolean; notFound?: boolean; error?: string }> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: no native Save As dialog. Fetch the book into the configured local folder (or the
    // app cache) via the same in-service download, then reveal it so the user can find/move it.
    try {
      const r = await serviceCall<{ handle?: string }>('triggerHbDownload', {
        bookId,
        localFolder: '',
        isOnline: navigator.onLine,
      })
      if (r?.handle) window.open(`/khs-file/${r.handle}`, '_blank')
      return { ok: true }
    } catch {
      return { ok: false } // dev convenience only
    }
  }
  return action('triggerHbSaveAs', { bookId, bookTitle, url })
}

/**
 * Open a native folder picker dialog.
 * Returns the selected folder path, or null if the user cancels.
 * Only available in hosted mode; returns null in dev mode.
 */
export async function pickFolder(title?: string): Promise<string | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: the browser's own directory picker can't hand back an absolute filesystem path
    // (only a handle + display name), so the service shows the real native shell dialog on
    // its desktop and returns the chosen path.
    try {
      const result = await serviceCall<{ path?: string; cancelled?: boolean }>('pickFolder', {
        value: title ?? '',
      })
      if (result.cancelled || !result.path) return null
      return result.path
    } catch {
      return null
    }
  }
  const result = await action<{ folderPath?: string; cancelled?: boolean; error?: string }>('pickFolder')
  if (result.cancelled || result.error || !result.folderPath) return null
  return result.folderPath
}

// ── Seforim DB path (settings page + setup wizard) ─────────────────────────────
// The KitveiHakodesh service owns the setting in both runtimes (registry value
// KitveiHakodesh\Database\Path) and restarts itself on every change so the DB, the user
// settings and the search indexes all re-resolve. The page reloads afterwards; the
// service client rides out the restart.

export interface DbPathInfo {
  path: string
  isCustom: boolean
  exists: boolean
  /** An Otzaria update of the DB stopped half-way (hot journal left behind); no book loads
   *  until Otzaria finishes it. */
  updateInterrupted?: boolean
  /** The database answers a query; false with `exists` true = the file cannot be read. */
  readable?: boolean
  /** With readable = false: the failure is transient (DB busy/locked by a writer). */
  readBusy?: boolean
  /** Which service instance answered. Changes when the service restarts after a path change. */
  instanceId?: string
}

/** The id of the instance that accepted the last path change — it is the one going DOWN.
 *  Null until a change has been made, or when the service predates the field. */
let switchedFromInstanceId: string | null = null

/**
 * Resolves once a NEW service instance answers after a path change (set / pick / clear).
 *
 * The instance that accepts the change keeps serving the OLD database for a few seconds
 * after it replied "restarting" (measured 0.5–3.7 s on the installed service), so reloading
 * on a fixed timer loaded the old library's catalog and then lost the connection mid-way.
 * Polls until the answering id differs from the one that accepted the change; a service
 * without the field (no id known) falls back to a short fixed wait. Bounded so a service
 * that never comes back still lets the page reload and show its unavailable banner.
 */
export async function waitForServiceRestart(timeoutMs = 30_000): Promise<void> {
  const old = switchedFromInstanceId
  switchedFromInstanceId = null
  if (!old) {
    await new Promise((r) => setTimeout(r, 800))
    return
  }
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const info = await serviceCall<DbPathInfo>('getSeforimDbPath')
      if (info?.instanceId && info.instanceId !== old) return
    } catch (err) {
      // The service going down mid-restart is the expected case; the client flags it
      // unavailable until the next answer clears it. A reason that will not fix itself
      // (install declined, will not start) means no new instance is coming: reload now.
      if (err instanceof ServiceUnavailableError && isPersistentReason(err.reason)) return
    }
    await new Promise((r) => setTimeout(r, 250))
  }
}

/** Current seforim DB path (+ whether user-set and whether the file exists). */
export async function getDbPathInfo(): Promise<DbPathInfo | null> {
  try {
    return await serviceCall<DbPathInfo>('getSeforimDbPath')
  } catch {
    return null
  }
}

/**
 * Hosted only: whether the product's Windows service is NOT the one serving right now (a
 * console copy is), so the settings page can offer to install it. Dev: never.
 */
export async function getServiceInstallOffer(): Promise<boolean> {
  if (typeof window.__webviewAction !== 'function') return false
  try {
    return (await action<{ canInstall?: boolean }>('getServiceStatus'))?.canInstall ?? false
  } catch {
    return false
  }
}

/** Hosted only: install / repair the Windows service (one admin prompt). False when declined or failed. */
export async function installService(): Promise<boolean> {
  try {
    return (await action<{ ok?: boolean }>('installService'))?.ok ?? false
  } catch {
    return false
  }
}

/**
 * Show the native open-file dialog for the seforim DB and persist the choice. Returns the
 * chosen path, or null when the user cancelled or it failed.
 *
 * Hosted: the WebView2 host owns the dialog (an installed service may run in a session with
 * no desktop to show one on), then the path goes to the service like a typed one. Dev: the
 * service shows the dialog on its own desktop and persists in one step.
 *
 * `throwSaveErrors`: a path was chosen but could not be saved (file not found, service down
 * past the brief retry) → throw instead of returning null, so the caller can tell the user.
 */
export async function pickDbPath(options: { throwSaveErrors?: boolean } = {}): Promise<string | null> {
  try {
    if (typeof window.__webviewAction === 'function') {
      const picked = await action<{ path?: string; cancelled?: boolean; error?: string }>('pickDbPath')
      if (picked?.cancelled || picked?.error || !picked?.path) return null
      await setDbPathRetryingWhileUnavailable(picked.path)
      return picked.path
    }
    const result = await serviceCall<{ path?: string; cancelled?: boolean; error?: string; instanceId?: string }>(
      'pickSeforimDbPath',
    )
    if (result?.cancelled || !result?.path) {
      if (result?.error) throw new Error(result.error)
      return null
    }
    if (result.error) throw new Error(result.error)
    switchedFromInstanceId = result.instanceId ?? null
    return result.path
  } catch (err) {
    console.error('[bridge] pickDbPath failed:', err)
    if (options.throwSaveErrors) throw err
    return null
  }
}

/** setDbPath, retried for a few seconds while the service is unreachable (e.g. restarting).
 *  A real error (file not found, ...) is thrown at once. */
async function setDbPathRetryingWhileUnavailable(path: string): Promise<void> {
  const deadline = Date.now() + 8000
  for (;;) {
    try {
      await setDbPath(path)
      return
    } catch (err) {
      if (!(err instanceof ServiceUnavailableError) || Date.now() >= deadline) throw err
      await new Promise((r) => setTimeout(r, 500))
    }
  }
}

/**
 * Persist a typed seforim DB path. The service writes the registry value and restarts
 * itself; throws when the file doesn't exist.
 */
export async function setDbPath(path: string): Promise<void> {
  const res = await serviceCall<{ path: string; error?: string; instanceId?: string }>('setSeforimDbPath', { path })
  if (res.error) throw new Error(res.error)
  switchedFromInstanceId = res.instanceId ?? null
}

/**
 * Reset the database path to the auto-resolved default (Zayit / Otzaria). The service
 * clears the registry value and restarts. Returns the resolved default path so the
 * frontend can update its display.
 */
export async function clearDbPath(): Promise<string | null> {
  try {
    const res = await serviceCall<{ path: string; instanceId?: string }>('clearSeforimDbPath')
    switchedFromInstanceId = res.instanceId ?? null
    return res.path ?? null
  } catch {
    return null
  }
}

/**
 * Clear the persisted HebrewBooks local folder setting (saves an empty string to the registry).
 */
export async function clearHbLocalFolder(): Promise<void> {
  setHbLocalFolderInRegistry('')
}

/**
 * Read whether the automatic update check is turned off. Backed by the SAME registry
 * key the KleiKodesh Word add-in uses (HKCU\...\KleiKodesh\UpdateChecker\TurnOffUpdates),
 * so one toggle governs both apps. Returns null when the service cannot answer.
 */
export async function getTurnOffUpdates(): Promise<boolean | null> {
  try {
    const res = await serviceCall<{ value?: boolean }>('getTurnOffUpdates')
    return res?.value ?? false
  } catch {
    return null
  }
}

/**
 * Persist the "turn off automatic updates" flag to the shared VSTO registry key.
 */
export async function setTurnOffUpdates(value: boolean): Promise<void> {
  // Awaited, not fire-and-forget: the settings page re-reads the update status right after.
  try {
    await serviceCall('setTurnOffUpdates', { value })
  } catch {
    /* the service banner covers an unreachable service */
  }
}

/**
 * The self-update state the service reports. Every field is '' when it does not apply:
 * readyVersion — a newer installer already downloaded and ready to run;
 * installedVersion — the installed copy's version ('' when this is not an installed copy);
 * checkError — why the last check could not run or failed ('' when it ran).
 */
export interface UpdateStatus {
  readyVersion: string
  installedVersion: string
  checkError: string
}

interface UpdateStatusReply {
  ready?: boolean
  version?: string
  installedVersion?: string
  checkError?: string
}

function toUpdateStatus(reply: UpdateStatusReply | undefined): UpdateStatus {
  return {
    readyVersion: reply?.ready ? reply.version ?? '' : '',
    installedVersion: reply?.installedVersion ?? '',
    checkError: reply?.checkError ?? '',
  }
}

/**
 * Ask the service whether a newer installer is already downloaded and ready. The service
 * checks once a week and downloads in the background; it never installs on its own.
 * readyVersion is '' while updates are turned off. Returns null when the service cannot answer.
 */
export async function getUpdateStatus(): Promise<UpdateStatus | null> {
  try {
    return toUpdateStatus(await serviceCall<UpdateStatusReply>('getUpdateStatus'))
  } catch {
    return null
  }
}

/**
 * Run the service's update check (and download, ~100 MB when a newer release exists) right
 * now instead of waiting for the weekly one. Can take MINUTES. A non-empty checkError means
 * the check could not run (dev/console instance) or failed (offline etc.). Returns null when
 * the service cannot answer.
 */
export async function checkForUpdateNow(): Promise<UpdateStatus | null> {
  try {
    return toUpdateStatus(await serviceCall<UpdateStatusReply>('checkForUpdateNow'))
  } catch {
    return null
  }
}

/**
 * Ask the service to start the downloaded installer (as the user, no arguments). The
 * installer itself asks to close Word and the app. Returns false when it could not start.
 */
export async function installReadyUpdate(): Promise<boolean> {
  try {
    const reply = await serviceCall<{ ok?: boolean }>('installUpdate')
    return reply?.ok === true
  } catch {
    return false
  }
}
