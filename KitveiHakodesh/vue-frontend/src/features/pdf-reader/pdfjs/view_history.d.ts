/** Types for the vendored PDF.js ViewHistory (view_history.js beside this file). */
export class ViewHistory {
  constructor(fingerprint: string, cacheSize?: number)
  _initializedPromise: Promise<void>
  database: unknown
  setMultiple(properties: Record<string, unknown>): Promise<void>
  getMultiple<T extends Record<string, unknown>>(properties: T): Promise<{ [K in keyof T]: unknown }>
}
