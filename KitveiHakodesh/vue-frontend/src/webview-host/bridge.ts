/**
 * Bridge to C# host for file operations.
 * All functions are no-ops / dev fallbacks when running outside the WebView2 host.
 *
 * In hosted mode, calls go via window.__webviewAction (injected by JsBridge.cs).
 * Push events from C# arrive via window.__onWebviewEvent (registered in db.ts).
 */

import { isHosted } from './seforimDb'
import { devPickPdf } from './devFallbacks'

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
}

// ── Hosted actions ────────────────────────────────────────────────────────────

/**
 * Open native file picker (PDF, Word, HTML formats).
 * For Word files, C# pushes a `localFileConversionStarted` event before replying,
 * so the tab can show a converting placeholder while waiting.
 * Returns null if the user cancels.
 */
export async function pickLocalFile(): Promise<LocalFileResult | null> {
  if (typeof window.__webviewAction !== 'function') return devPickPdf()
  const res = await action<{
    cancelled?: boolean
    url?: string
    fileName?: string
    filePath?: string
    error?: string
  }>('pickFile')
  if (res.cancelled || res.error || !res.url) return null
  return { url: res.url, fileName: res.fileName!, filePath: res.filePath! }
}

/**
 * Restore a local file tab from a persisted file path.
 * C# re-registers the virtual host and returns the URL.
 */
export async function restoreLocalFile(filePath: string): Promise<LocalFileRestoreResult | null> {
  if (!isHosted) return null
  const res = await action<{ url?: string; error?: string }>('restoreLocalFile', { filePath })
  if (res.error || !res.url) return null
  return { url: res.url }
}

/**
 * Restore a HebrewBooks PDF tab from a persisted book ID.
 * C# checks the cache; if evicted, re-downloads.
 * Returns null on failure.
 */
export async function restoreHbPdf(
  bookId: string,
  bookTitle: string,
  tabId: string,
): Promise<{ url: string } | { redownload: true } | null> {
  if (!isHosted) return null
  const res = await action<{ url?: string; redownload?: boolean; error?: string }>('restoreHbPdf', {
    bookId,
    bookTitle,
    tabId,
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
    window.location.reload()
    return
  }
  await action('DeleteFtsIndex').catch(() => {})
  await action('resetSettings').catch(() => {})
  action('reload').catch(() => window.location.reload())
}

/**
 * Reset the FTS search index on the C# side.
 */
export async function resetSearchIndex(): Promise<void> {
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
 * Returns books as plain objects — caller casts to HebrewBook[].
 */
export function hbSearch(query: string): Promise<{ books?: unknown[]; error?: string }> {
  return action<{ books?: unknown[]; error?: string }>('hbSearch', { query })
}

/**
 * Notify C# that the file search page has loaded.
 * C# replies immediately with { isReady: bool }.
 * If isReady=false, C# launches Everything in the background and pushes
 * fileSystemIndexingStatus { isIndexing: false } when ready.
 */
export function fileSystemSearchPageLoad(): Promise<{ isReady: boolean }> {
  return action('fileSystemSearchPageLoad')
}

/**
 * Search the file system using the Everything index.
 * Results are restricted to MS Word and document formats (FilterToDocumentTypes = true).
 * Returns up to `max` results (default 200).
 */
export function fileSystemSearch(
  query: string,
  max = 200,
): Promise<{
  results?: Array<{ fileName: string; path: string }>
  total?: number
  error?: string
}> {
  return action('fileSystemSearch', { query, max })
}

/**
 * Trigger a HebrewBooks PDF download to the cache, then open it.
 */
export function triggerHbDownload(
  bookId: string,
  bookTitle: string,
  url: string,
  tabId: string,
): Promise<{ ok?: boolean }> {
  return action<{ ok?: boolean }>('triggerHbDownload', { bookId, bookTitle, url, tabId })
}

/**
 * Trigger a HebrewBooks PDF Save As dialog.
 */
export function triggerHbSaveAs(
  bookId: string,
  bookTitle: string,
  url: string,
): Promise<{ ok?: boolean }> {
  return action<{ ok?: boolean }>('triggerHbSaveAs', { bookId, bookTitle, url })
}
