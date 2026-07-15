/**
 * Dev-mode fallbacks for host operations.
 * These are only ever called when running outside the C# WebView2 host (browser dev mode).
 * Nothing in this file should be imported in production paths — all callers guard with
 * `typeof window.__webviewXxx === 'function'` or `isHosted` before falling back here.
 */

import type { LocalFileResult } from './bridge'

// All dev DB access (seforim, dictionary, user-settings) now goes through the
// KitveiHakodesh service (see seforimApi.ts / dictionaryDb.ts / userSettingsDb.ts).
// The old better-sqlite3 dev worker has been removed — no fetch-based DB transports here.

/** Browser file input fallback for pickFile() — accepts PDF, HTML, and text files. */
export function devPickPdf(): Promise<LocalFileResult | null> {
  return new Promise((resolve) => {
    const input = Object.assign(document.createElement('input'), {
      type: 'file',
      accept: '.pdf,.htm,.html,.txt',
    })
    input.onchange = () => {
      const file = input.files?.[0]
      if (!file) { resolve(null); return }
      resolve({ url: URL.createObjectURL(file), fileName: file.name, filePath: '' })
    }
    input.oncancel = () => resolve(null)
    input.click()
  })
}

// File-system ("Everything"-style) search moved to the KitveiHakodesh service —
// see fileSystemSearch() in bridge.ts, which calls serviceCall('locateDocuments').
