/**
 * Access layer for the user settings database (user_settings.db).
 * Exposes queryUserSettings() and executeUserSettings() — mirrors the seforimDb pattern.
 *
 * The KitveiHakodesh service owns the database in both runtimes. Params and rows are plain
 * MessagePack values. Row keys pass through the reply decoder's key rename (first letter
 * lower-cased), so SELECTs must name their columns in camelCase — every column already is.
 */

import { serviceCall } from './serviceClient'

export async function queryUserSettings<T = unknown>(
  sql: string,
  params: unknown[] = [],
): Promise<T[]> {
  const r = await serviceCall<{ rows: T[] }>('userSettingsQuery', { sql, params })
  return r.rows
}

export async function executeUserSettings(
  sql: string,
  params: unknown[] = [],
): Promise<number> {
  return (await serviceCall<{ lastInsertId: number }>('userSettingsExecute', { sql, params })).lastInsertId
}
