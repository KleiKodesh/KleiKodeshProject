/**
 * Access layer for the user settings database (user_settings.db).
 * Exposes queryUserSettings() and executeUserSettings() — mirrors the seforimDb pattern.
 *
 * The KitveiHakodesh service owns the database in both runtimes. This raw-SQL path stays
 * JSON inside the MessagePack envelope (dynamic params + arbitrary row shapes): params ride
 * as a JSON string, rows come back as a JSON string — no point re-encoding already-JSON data.
 */

import { serviceCall } from './serviceClient'

export async function queryUserSettings<T = unknown>(
  sql: string,
  params: unknown[] = [],
): Promise<T[]> {
  const r = await serviceCall<{ rowsJson: string }>('userSettingsQuery', { sql, paramsJson: JSON.stringify(params) })
  return JSON.parse(r.rowsJson) as T[]
}

export async function executeUserSettings(
  sql: string,
  params: unknown[] = [],
): Promise<number> {
  return (await serviceCall<{ lastInsertId: number }>('userSettingsExecute', { sql, paramsJson: JSON.stringify(params) })).lastInsertId
}
