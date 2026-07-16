/**
 * Access layer for the user settings database (user_settings.db).
 * Exposes queryUserSettings() and executeUserSettings() — mirrors the seforimDb pattern.
 *
 * In the C# host: uses window.__webviewUserSettingsQuery / __webviewUserSettingsExecute
 * injected by JsBridge.cs.
 * In dev mode: routes through the KitveiHakodesh service (read + write).
 */

import { serviceCall } from './serviceClient'

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
  // Dev — through the KitveiHakodesh service. This raw-SQL path stays JSON (dynamic params +
  // arbitrary row shapes): params ride as a JSON string, rows come back as a JSON string —
  // no point re-encoding already-JSON data as MessagePack.
  const r = await serviceCall<{ rowsJson: string }>('userSettingsQuery', { sql, paramsJson: JSON.stringify(params) })
  return JSON.parse(r.rowsJson) as T[]
}

export async function executeUserSettings(
  sql: string,
  params: unknown[] = [],
): Promise<number> {
  if (typeof window.__webviewUserSettingsExecute === 'function') {
    return (await window.__webviewUserSettingsExecute(sql, params)).lastInsertId
  }
  // Dev — through the KitveiHakodesh service (params as a JSON string; see queryUserSettings).
  return (await serviceCall<{ lastInsertId: number }>('userSettingsExecute', { sql, paramsJson: JSON.stringify(params) })).lastInsertId
}
