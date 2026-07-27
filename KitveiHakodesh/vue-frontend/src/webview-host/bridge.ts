/**
 * Bridge to C# host for file operations.
 * All functions are no-ops / dev fallbacks when running outside the WebView2 host.
 *
 * In hosted mode, calls go via window.__webviewAction (injected by JsBridge.cs).
 * Push events from C# arrive via window.__onWebviewEvent (registered in db.ts).
 */

import { isHosted, emitWebviewEvent } from './seforimDb'
import { serviceCall, serviceCallVoid } from './serviceClient'
import { decodeTextDetectEncoding } from '@/utils/textEncoding'

declare global {
  interface Window {
    __webviewQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewPickDbPath?: () => void
    __webviewSetDbPath?: (path: string) => Promise<{ path: string }>
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
    __webviewDbPath?: string
    __webviewDbReady?: boolean
    __webviewShowPopOut?: boolean
    __webviewHbLocalFolder?: string
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
 * True only when a native chrome tab strip (FluentChromeTabsForm) is actually
 * present to mirror the tabs — i.e. the standalone/demo WebView2 host, not the
 * VSTO task pane and not the dev browser. This is the exact condition under which
 * initTabMirror() wires up the strip, so UI that delegates to the native strip
 * (e.g. the Ctrl+T tab list) can gate on the same flag.
 *
 * The dev browser has no strip, so it must behave like VSTO (Vue-owned tab list),
 * not like the demo — hence !isHosted-in-dev alone is not enough; we require the
 * bridge action channel too.
 */
export const hasNativeChromeTabs =
  isHosted && !isVstoEnvironment && typeof window.__webviewAction === 'function'

function action<T>(name: string, args?: object): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  return window.__webviewAction(name, args) as Promise<T>
}

/**
 * Call a C# bridge action with positional params (used by search/indexing).
 * The bridge receives params as an array, not a named object.
 */
export function callBridgeAction<T>(name: string, ...params: unknown[]): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  return window.__webviewAction(name, params as unknown as object) as Promise<T>
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
 */
export async function pickLocalFile(openInNewTab = false): Promise<LocalFileResult | null> {
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
        if (!isDirect)
          emitWebviewEvent({ event: 'localFileError', filePath, message: r?.error || 'פתיחת הקובץ נכשלה' })
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
        ? `/khs-file/${r.folderHandle}/${servedName}`
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
    } catch {
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
    }>('pickFile')
    if (res.cancelled || res.error || !res.url) return null
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
      if (!r?.handle) return null
      // The service reports what it will actually serve: a converted Word doc comes back as
      // *.pdf (Word) or *.html (Office-free fallback) — the viewer route must follow suit.
      const servedExt = r.fileName?.toLowerCase().endsWith('.html') ? '.html' : '.pdf'
      const kind = servedExt === '.html' ? ('html' as const) : ('pdf' as const)
      // HTML files get a folder-scoped URL so siblings load; PDF gets single-file handle.
      const url = kind === 'html' && r.folderHandle
        ? `/khs-file/${r.folderHandle}/${r.fileName}`
        : `/khs-file/${r.handle}`
      return { url, kind }
    } catch {
      return null
    }
  }
  const res = await action<{ url?: string; error?: string }>('restoreLocalFile', { filePath })
  if (res.error || !res.url) return null
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
 * Restore a HebrewBooks PDF tab from a persisted book ID.
 * C# checks the local folder first, then the cache; if neither has it, re-downloads.
 * Returns null on failure.
 */
export async function restoreHbPdf(
  bookId: string,
  bookTitle: string,
  tabId: string,
  localFolder?: string,
): Promise<{ url: string } | { redownload: true } | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: local/cache-only lookup via the service (no download). A miss returns redownload —
    // and, matching the hosted HandleRestoreHbPdf (which self-initiates the re-download and
    // finishes via an hbPdfReady push), we kick off the in-service download here and replay the
    // hbPdfReady/hbPdfCancelled events so the store's _converting tab is resolved.
    try {
      const r = await serviceCall<{ handle?: string; redownload?: boolean; error?: string }>(
        'restoreHbPdf',
        { bookId, localFolder: localFolder || '' },
      )
      if (r?.handle) return { url: `/khs-file/${r.handle}` }
      if (r?.redownload) {
        // The service builds its own download URL from the book id, so the url arg is unused here.
        triggerHbDownload(bookId, bookTitle, '', tabId, localFolder, navigator.onLine)
        return { redownload: true }
      }
      return null
    } catch {
      return null
    }
  }
  const res = await action<{ url?: string; redownload?: boolean; error?: string }>('restoreHbPdf', {
    bookId,
    bookTitle,
    tabId,
    localFolder: localFolder || '',
  })
  if (res.error) return null
  if (res.redownload) return { redownload: true }
  if (res.url) return { url: res.url }
  return null
}

/**
 * Notify C# that a local file tab was closed so it can decrement the virtual host ref count.
 * Only relevant for local files (not cache-based files).
 */
export function disposeLocalFileHost(filePath: string): void {
  if (!isHosted || !filePath) return
  action('disposeLocalFileHost', { filePath }).catch(() => {})
}

/**
 * Toggle the host user control visibility — pops the viewer out into a floating window
 * or returns it to the VSTO task pane / host form.
 */
export function togglePopOut(): void {
  if (!isHosted) return
  action('TogglePopOut').catch(() => {})
}

/**
 * Full app reset — deletes the FTS index, resets C# settings, then reloads.
 * Call tabStore.resetAll() before this to schedule the IDB wipe.
 */
export async function resetHostApp(): Promise<void> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: the C# host doesn't exist, so reset both indexes through the service and
    // clear local settings (IDB is already wiped by tabStore.resetAll()), then reload.
    await serviceCall('ftsResetIndex').catch(() => {})
    await serviceCall('resetDocumentLocatorIndex').catch(() => {})
    try { localStorage.clear() } catch { /* ignore */ }
    window.location.reload()
    return
  }
  await action('DeleteFtsIndex').catch(() => {})
  await action('resetSettings').catch(() => {})
  action('reload').catch(() => window.location.reload())
}

/**
 * Trigger a full DocumentLocator index rebuild on the C# side.
 * The service wipes its Lucene index and re-crawls the NTFS MFT from scratch.
 * Progress is pushed via fileSystemIndexingStatus events while the rebuild runs.
 */
export async function resetDocumentLocatorIndex(): Promise<void> {
  if (typeof window.__webviewAction !== 'function') {
    await serviceCall('resetDocumentLocatorIndex').catch(() => {}) // dev: via KitveiHakodesh service
    return
  }
  await action('ResetDocumentLocatorIndex').catch(() => {})
}

/**
 * Open the excluded folders manager dialog (WinForms, shown by C#).
 * The dialog lets the user add/remove folders excluded from the file-system index.
 * Returns { saved: true } when the user confirms changes, { saved: false } on cancel,
 * or null on error.
 */
export async function openExcludedFoldersManager(): Promise<{ saved: boolean } | null> {
  if (typeof window.__webviewAction !== 'function') return null
  try {
    const result = await action<{ saved?: boolean; error?: string }>('openExcludedFoldersManager')
    if (result.error) return null
    return { saved: result.saved ?? false }
  } catch {
    return null
  }
}

/**
 * Read/write the excluded folders list in dev. The service persists it to
 * excluded_folders.json inside the file-search index directory using the same shared
 * ExcludedFoldersPersistence the hosted DocumentLocator service uses, and applies it at
 * search time — so a change takes effect immediately with no reindex.
 *
 * Hosted mode does not use these: it opens the native WinForms manager instead
 * (openExcludedFoldersManager), which owns both the UI and the persistence.
 */
export async function getExcludedFolders(): Promise<string[]> {
  if (typeof window.__webviewAction === 'function') return []
  try {
    const result = await serviceCall<{ folders?: string[] }>('getExcludedFolders')
    return result?.folders ?? []
  } catch {
    return []
  }
}

/** Persist the full replacement list. Returns the list as it stands on disk afterwards. */
export async function setExcludedFolders(folders: string[]): Promise<string[]> {
  if (typeof window.__webviewAction === 'function') return []
  const result = await serviceCall<{ folders?: string[]; error?: string }>('setExcludedFolders', {
    folders,
  })
  if (result?.error) throw new Error(result.error)
  return result?.folders ?? []
}

/**
 * Reset the FTS search index on the C# side.
 */
export async function resetSearchIndex(): Promise<void> {
  if (typeof window.__webviewAction !== 'function') {
    await serviceCall('ftsResetIndex').catch(() => {}) // dev: via KitveiHakodesh service
    return
  }
  await action('ResetFtsIndex').catch(() => {})
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
  if (!isHosted) return
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
  if (typeof window.__webviewAction !== 'function') {
    // Dev mode: query the KitveiHakodesh service's bundled catalog.
    return serviceCall<{ books: unknown[] }>('hbSearch', args).catch((err) => ({
      error: err instanceof Error ? err.message : 'Search error',
    }))
  }
  return action<{ books?: unknown[]; error?: string }>('hbSearch', args)
}

/**
 * Notify C# to warm up the DocumentLocator service in the background.
 * Fire-and-forget — no reply is needed. By the time the user types a query
 * the service will likely already be running and the index ready.
 */
export function fileSystemSearchWarmup(): void {
  if (typeof window.__webviewAction !== 'function') {
    // Dev mode: warm up the DocumentLocator via the KitveiHakodesh service.
    serviceCallVoid('locateDocumentsWarmup')
    return
  }
  action('fileSystemSearchWarmup').catch(() => {})
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
  results?: Array<{ fileName: string; path: string; modifiedDate?: number }>
  total?: number
  error?: string
}> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev mode: query the KitveiHakodesh service for documents. The service owns
    // the DocumentLocator delegation and the result shaping; we just ask for it.
    return serviceCall<{
      results: Array<{ fileName: string; path: string; modifiedDate?: number }>
      total: number
    }>('locateDocuments', { query, max }).catch((err) => ({
      error: err instanceof Error ? err.message : 'Search error',
    }))
  }
  return action('fileSystemSearch', { query, max })
}

/**
 * Export book content as HTML to a new Word document.
 * C# opens Word (or reuses a running instance), creates a blank document,
 * and inserts the provided HTML as the document content.
 */
export function exportToWord(html: string, title: string = ''): Promise<{ ok?: boolean; error?: string }> {
  return action<{ ok?: boolean; error?: string }>('exportToWord', { html, title })
}

/**
 * Paste into Word at the current cursor position by reading from the Windows clipboard.
 * The caller must have already set the clipboard via execCopyHtml / copyToClipboard
 * before calling this — the C# side calls Selection.Paste() which picks up whatever
 * is on the clipboard, so no HTML needs to be sent over the bridge.
 */
export function pasteIntoWord(): Promise<{ ok?: boolean; error?: string }> {
  return action<{ ok?: boolean; error?: string }>('pasteIntoWord', {})
}

/**
 * Place a PNG image on the Windows clipboard via the C# host.
 * The browser's navigator.clipboard.write() for images is unreliable inside
 * WebView2, so C# does it with System.Windows.Forms.Clipboard instead.
 * `dataUrl` must be a PNG data: URL (canvas.toDataURL('image/png')).
 * Rejects when the bridge is unavailable (dev/browser) — callers fall back.
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
 * Fire-and-forget — no meaningful return value.
 */
export function setTheme(
  isDark: boolean,
  chromeColor?: string,
  accentColor?: string,
  borderColor?: string,
): void {
  if (typeof window.__webviewAction !== 'function') return
  action('setTheme', {
    isDark,
    ...(chromeColor ? { chromeColor } : {}),
    ...(accentColor ? { accentColor } : {}),
    ...(borderColor ? { borderColor } : {}),
  }).catch(() => {})
}

// ── Native chrome tabs mirror ────────────────────────────────────────────────

export interface MirroredTab {
  id: string
  title: string
  /** Full breadcrumb ("prefix: title · toc path") for the native tab-list dropdown; equals title when no path. */
  listTitle: string
  /** "prefix: title" — strip caption used only when the tab is wide enough to fit it. */
  stripTitle: string
  pane: 1 | 2
}

/** A recently opened document (not currently open) for the native tab-list dropdown. */
export interface MirroredRecentItem {
  /** Stable key from recentlyOpenedStore — echoed back by chromeRecentActivated. */
  key: string
  title: string
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
  /** Recently opened documents for the dropdown's "נסגרו לאחרונה" section. */
  recent: MirroredRecentItem[]
}

/**
 * Push a full snapshot of the Vue tab store to the C# host so the native chrome
 * tab strip can mirror it (membership, titles, per-pane active tabs, split state),
 * plus recently opened documents for the dropdown. Fire-and-forget.
 */
export function notifyTabsChanged(snapshot: TabsSnapshot): void {
  if (typeof window.__webviewAction !== 'function') return
  action('tabsChanged', { ...snapshot }).catch(() => {})
}

/**
 * Trigger a HebrewBooks PDF download to the cache, then open it.
 * If localFolder is provided, C# will first check for {localFolder}\{bookId}.pdf
 * before falling back to the download flow.
 */
export function triggerHbDownload(
  bookId: string,
  bookTitle: string,
  url: string,
  tabId: string,
  localFolder?: string,
  isOnline?: boolean,
): Promise<{ ok?: boolean }> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: the service downloads the PDF ENTIRELY in C# (HttpClient — no browser download
    // interception) and returns a GET /file capability handle. The hosted app finishes this
    // flow with an hbPdfReady/hbPdfCancelled PUSH; dev has no C# push channel, so we replay
    // the same events locally (emitWebviewEvent) after the round-trip. localFileStore's
    // finishHbDownload/cancelHbDownload listeners then run unchanged.
    ;(async () => {
      try {
        const r = await serviceCall<{
          handle?: string
          notFound?: boolean
          noInternet?: boolean
          cancelled?: boolean
          error?: string
        }>('triggerHbDownload', { bookId, localFolder: localFolder || '', isOnline: isOnline !== false })
        if (r?.handle) {
          emitWebviewEvent({ event: 'hbPdfReady', url: `/khs-file/${r.handle}`, bookId, bookTitle, tabId })
        } else if (r?.cancelled) {
          // User pressed ביטול — the tab was already reset by cancelConversion; nothing to show.
          emitWebviewEvent({ event: 'hbPdfCancelled', tabId, cancelled: true })
        } else {
          emitWebviewEvent({
            event: 'hbPdfCancelled',
            tabId,
            notFound: !!r?.notFound,
            noInternet: !!r?.noInternet,
          })
        }
      } catch {
        emitWebviewEvent({ event: 'hbPdfCancelled', tabId, noInternet: !navigator.onLine })
      }
    })()
    return Promise.resolve({ ok: true })
  }
  return action<{ ok?: boolean }>('triggerHbDownload', {
    bookId,
    bookTitle,
    url,
    tabId,
    localFolder: localFolder || '',
    isOnline: isOnline !== false,
  })
}

/**
 * Poll the live byte progress of an in-flight HebrewBooks download (dev only — the download runs
 * in the service, streamed). Returns { active, received, total } where total 0 means the server
 * sent no Content-Length (show MB, not %), or null when nothing is downloading / in hosted mode
 * (the WebView2 download has its own native dialog).
 */
export async function getHbDownloadProgress(
  bookId: string,
): Promise<{ active: boolean; received: number; total: number } | null> {
  if (typeof window.__webviewAction === 'function') return null
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
 * Abort an in-flight HebrewBooks download (the ביטול button). Dev only — trips the service's
 * per-book cancellation so the streamed download stops and its partial file is cleaned up.
 * Fire-and-forget. Hosted mode uses the WebView2 download's own cancel, so this is a no-op there.
 */
export function cancelHbDownload(bookId: string): void {
  if (typeof window.__webviewAction === 'function' || !bookId) return
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
  if (typeof window.__webviewAction !== 'function') {
    return serviceCall<{ existingIds?: string[]; error?: string }>('checkHbLocalFiles', {
      bookIds,
      localFolder,
    }).catch(() => ({ existingIds: [] }))
  }
  return action<{ existingIds?: string[]; error?: string }>('checkHbLocalFiles', {
    bookIds,
    localFolder,
  })
}

/**
 * Delete a HebrewBooks PDF from the user's configured local folder.
 * Returns { ok: true } on success, { notFound: true } if the file is not there,
 * or { error: "..." } on failure.
 */
export function deleteHbLocalFile(
  bookId: string,
  localFolder: string,
): Promise<{ ok?: boolean; notFound?: boolean; error?: string }> {
  if (typeof window.__webviewAction !== 'function') {
    return serviceCall<{ ok?: boolean; notFound?: boolean; error?: string }>('deleteHbLocalFile', {
      bookId,
      localFolder,
    })
  }
  return action<{ ok?: boolean; notFound?: boolean; error?: string }>('deleteHbLocalFile', {
    bookId,
    localFolder,
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
 * Get/set the HebrewBooks local download folder in the SHARED registry
 * (HKCU\...\KitveiHakodesh\HebrewBooks\LocalFolder — the exact key the hosted app's AppSettings
 * uses). This keeps dev and the hosted app agreeing on the folder. Hosted mode reads the value
 * injected as window.__webviewHbLocalFolder and persists via the C# host, so these are dev-only.
 */
export async function getHbLocalFolderFromRegistry(): Promise<string> {
  if (typeof window.__webviewAction === 'function') return window.__webviewHbLocalFolder || ''
  try {
    const r = await serviceCall<{ value?: string }>('getHbLocalFolder')
    return r?.value || ''
  } catch {
    return ''
  }
}

export function setHbLocalFolderInRegistry(path: string): void {
  if (typeof window.__webviewAction === 'function') return
  serviceCallVoid('setHbLocalFolder', { value: path })
}

/**
 * Trigger a HebrewBooks PDF Save As dialog.
 */
export function triggerHbSaveAs(
  bookId: string,
  bookTitle: string,
  url: string,
): Promise<{ ok?: boolean }> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: no native Save As dialog. Fetch the book into the configured local folder (or the
    // app cache) via the same in-service download, then reveal it so the user can find/move it.
    ;(async () => {
      try {
        const r = await serviceCall<{ handle?: string }>('triggerHbDownload', {
          bookId,
          localFolder: '',
          isOnline: navigator.onLine,
        })
        if (r?.handle) window.open(`/khs-file/${r.handle}`, '_blank')
      } catch {
        /* ignore — dev convenience only */
      }
    })()
    return Promise.resolve({ ok: true })
  }
  return action<{ ok?: boolean }>('triggerHbSaveAs', { bookId, bookTitle, url })
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
// Hosted: the C# host owns the setting. Dev: the KitveiHakodesh service owns it —
// BOTH persist to the same registry value (KitveiHakodesh\Database\Path), so the
// choice made in either mode is the one the app and the service agree on.

export interface DbPathInfo {
  path: string
  isCustom: boolean
  exists: boolean
}

/** Current seforim DB path (+ whether user-set and whether the file exists). */
export async function getDbPathInfo(): Promise<DbPathInfo | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev — ask the service.
    try {
      return await serviceCall<DbPathInfo>('getSeforimDbPath')
    } catch {
      return null
    }
  }
  const p = window.__webviewDbPath
  return p ? { path: p, isCustom: true, exists: true } : null
}

/**
 * Show a native open-file dialog for the seforim DB and persist the choice (dev only).
 * The hosted app has `window.__webviewPickDbPath`; a browser has no equivalent, so the
 * service shows the real dialog on its desktop, writes the same registry value, and
 * restarts. Returns the chosen path, or null when the user cancelled or it failed.
 */
export async function pickDbPathDev(): Promise<string | null> {
  try {
    const result = await serviceCall<{ path?: string; cancelled?: boolean; error?: string }>(
      'pickSeforimDbPath',
    )
    if (result?.cancelled || result?.error || !result?.path) return null
    return result.path
  } catch {
    return null
  }
}

/**
 * Persist a new seforim DB path. Dev: the service writes the registry value and
 * restarts itself to re-resolve everything (DB, user settings, FTS — a stale FTS
 * index is auto-detected and rebuilt); throws when the file doesn't exist.
 */
export async function setDbPathDev(path: string): Promise<void> {
  const res = await serviceCall<{ path: string; error?: string }>('setSeforimDbPath', { path })
  if (res.error) throw new Error(res.error)
}

/**
 * Reset the database path to the auto-resolved default (Zayit / Otzaria).
 * Hosted: C# reopens the DB and resets the search index if the path changed.
 * Dev: the service clears the registry value and restarts.
 * Returns the resolved default path so the frontend can update its display.
 */
export async function clearDbPath(): Promise<string | null> {
  if (typeof window.__webviewAction !== 'function') {
    try {
      const res = await serviceCall<{ path: string }>('clearSeforimDbPath')
      return res.path ?? null
    } catch {
      return null
    }
  }
  const result = await action<{ path?: string; error?: string }>('clearDbPath')
  if (result.error || !result.path) return null
  return result.path
}

/**
 * Clear the persisted HebrewBooks local folder setting (saves an empty string to the registry).
 */
export async function clearHbLocalFolder(): Promise<void> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: clear the shared registry value (empty string) so the hosted app agrees.
    setHbLocalFolderInRegistry('')
    return
  }
  await action('clearHbLocalFolder').catch(() => {})
}

/**
 * Read whether the automatic update check is turned off. Backed by the SAME registry
 * key the KleiKodesh Word add-in uses (HKCU\...\KleiKodesh\UpdateChecker\TurnOffUpdates),
 * so one toggle governs both apps. Returns null in dev/browser (no host).
 */
export async function getTurnOffUpdates(): Promise<boolean | null> {
  if (typeof window.__webviewAction !== 'function') {
    // Dev: the service reads the same shared registry value, so the toggle agrees with
    // the hosted app and the Word add-in.
    try {
      const res = await serviceCall<{ value?: boolean }>('getTurnOffUpdates')
      return res?.value ?? false
    } catch {
      return null
    }
  }
  try {
    const res = await action<{ value?: boolean; error?: string }>('getTurnOffUpdates')
    if (res.error) return null
    return res.value ?? false
  } catch {
    return null
  }
}

/**
 * Persist the "turn off automatic updates" flag to the shared VSTO registry key.
 */
export async function setTurnOffUpdates(value: boolean): Promise<void> {
  if (typeof window.__webviewAction !== 'function') {
    await serviceCallVoid('setTurnOffUpdates', { value })
    return
  }
  await action('setTurnOffUpdates', { value }).catch(() => {})
}
