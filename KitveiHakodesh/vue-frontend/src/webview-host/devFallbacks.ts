/**
 * Dev-mode fallbacks for host operations.
 * These are only ever called when running outside the C# WebView2 host (browser dev mode).
 * Nothing in this file should be imported in production paths — all callers guard with
 * `typeof window.__webviewXxx === 'function'` or `isHosted` before falling back here.
 */

import type { LocalFileResult } from './bridge'

// ── Database fallbacks ────────────────────────────────────────────────────────

/** Hits the Vite dev middleware at /query (main seforim DB). */
export async function devQuery<T = unknown>(sql: string, params: unknown[]): Promise<T[]> {
  const res = await fetch('/query', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!res.ok) throw new Error(`DB query failed: ${res.status} ${res.statusText}`)
  return (await res.json()).rows as T[]
}

/** Hits the Vite dev middleware at /query-dict (dictionary DB — entries, senses, related). */
export async function devQueryDict<T = unknown>(sql: string, params: unknown[]): Promise<T[]> {
  const res = await fetch('/query-dict', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  const json = await res.json()
  if (!res.ok) throw new Error(`Dict query failed: ${json.error ?? res.statusText}`)
  return json.rows as T[]
}

/** Browser file input fallback for pickFile() — accepts PDF only. */
export function devPickPdf(): Promise<LocalFileResult | null> {
  return new Promise((resolve) => {
    const input = Object.assign(document.createElement('input'), {
      type: 'file',
      accept: '.pdf,.htm,.html',
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
