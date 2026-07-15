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
  // Dev — through the KitveiHakodesh service.
  return (await serviceCall<{ rows: T[] }>('userSettingsQuery', { sql, params })).rows
}

export async function executeUserSettings(
  sql: string,
  params: unknown[] = [],
): Promise<number> {
  if (typeof window.__webviewUserSettingsExecute === 'function') {
    return (await window.__webviewUserSettingsExecute(sql, params)).lastInsertId
  }
  // Dev — through the KitveiHakodesh service.
  return (await serviceCall<{ lastInsertId: number }>('userSettingsExecute', { sql, params })).lastInsertId
}
