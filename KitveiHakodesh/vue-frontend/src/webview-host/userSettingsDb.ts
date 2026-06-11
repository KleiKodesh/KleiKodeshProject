/**
 * Access layer for the user settings database (user_settings.db).
 * Exposes queryUserSettings() and executeUserSettings() — mirrors the seforimDb pattern.
 *
 * In the C# host: uses window.__webviewUserSettingsQuery / __webviewUserSettingsExecute
 * injected by JsBridge.cs.
 * In dev mode: falls back to /query-user-settings on the Vite dev middleware.
 */

declare global {
  interface Window {
    __webviewUserSettingsQuery?: (
      sql: string,
      params: unknown[],
    ) => Promise<{ rows: unknown[] }>
    __webviewUserSettingsExecute?: (
      sql: string,
      params: unknown[],
    ) => Promise<{ lastInsertId: number }>
  }
}

export async function queryUserSettings<T = unknown>(
  sql: string,
  params: unknown[] = [],
): Promise<T[]> {
  if (typeof window.__webviewUserSettingsQuery === 'function') {
    return (await window.__webviewUserSettingsQuery(sql, params)).rows as T[]
  }
  // Dev fallback
  const response = await fetch('/query-user-settings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!response.ok) throw new Error(`User settings query failed: ${response.status}`)
  return (await response.json()).rows as T[]
}

export async function executeUserSettings(
  sql: string,
  params: unknown[] = [],
): Promise<number> {
  if (typeof window.__webviewUserSettingsExecute === 'function') {
    return (await window.__webviewUserSettingsExecute(sql, params)).lastInsertId
  }
  // Dev fallback
  const response = await fetch('/execute-user-settings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!response.ok) throw new Error(`User settings execute failed: ${response.status}`)
  return (await response.json()).lastInsertId as number
}
