/**
 * Per-addin sandboxed storage backing the addin `storage.*` API.
 *
 * Each addin gets its own IndexedDB database keyed by its addin id (the plugin
 * folder name). Addins can only ever touch their own database — this grants no
 * access to any app data, which is why `storage.*` stays enabled under the
 * data-query-only policy.
 *
 * Each addin may hold ADDIN_STORAGE_BUDGET_BYTES. A `sizes` store beside `data` keeps each
 * key's approximate size (its JSON length, 2 bytes a char), so a write is checked against
 * the budget in its own transaction without reading every value back. A write that would
 * go over is refused with an error the addin sees.
 */

const ADDIN_STORAGE_BUDGET_BYTES = 5 * 1024 * 1024

/** Approximate stored size: JSON length in UTF-16 bytes. Unserialisable values count as 0. */
function approximateBytes(value: unknown): number {
  try { return (JSON.stringify(value)?.length ?? 0) * 2 } catch { return 0 }
}

/** One connection per addin for the session — an unclosed one per call blocked deleteDatabase. */
const openDatabases = new Map<string, Promise<IDBDatabase>>()

function openAddinDatabase(addinId: string): Promise<IDBDatabase> {
  let open = openDatabases.get(addinId)
  if (!open) {
    open = openAddinDatabaseOnce(addinId)
    openDatabases.set(addinId, open)
    open.catch(() => openDatabases.delete(addinId))
  }
  return open
}

function openAddinDatabaseOnce(addinId: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(`app-addin-storage-${addinId}`, 2)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains('data'))
        request.result.createObjectStore('data')
      // Version 2: key -> approximate bytes. Data written before it is not counted.
      if (!request.result.objectStoreNames.contains('sizes'))
        request.result.createObjectStore('sizes')
    }
    request.onsuccess = () => {
      const database = request.result
      // Someone else deletes or upgrades this database: let go, reopen on the next call.
      database.onversionchange = () => {
        database.close()
        openDatabases.delete(addinId)
      }
      resolve(database)
    }
    request.onerror = () => reject(request.error)
  })
}

export async function addinStorageGet(addinId: string, key: string): Promise<unknown> {
  const database = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const request = database.transaction('data').objectStore('data').get(key)
    request.onsuccess = () => resolve(request.result ?? null)
    request.onerror = () => reject(request.error)
  })
}

export async function addinStorageSet(addinId: string, key: string, value: unknown): Promise<void> {
  const database = await openAddinDatabase(addinId)
  const bytes = approximateBytes(value)
  return new Promise((resolve, reject) => {
    const transaction = database.transaction(['data', 'sizes'], 'readwrite')
    const sizes = transaction.objectStore('sizes')
    let refused: Error | null = null
    const sizesRequest = sizes.openCursor()
    let total = 0
    sizesRequest.onsuccess = () => {
      const cursor = sizesRequest.result
      if (cursor) {
        if (cursor.key !== key) total += cursor.value as number
        cursor.continue()
        return
      }
      if (total + bytes > ADDIN_STORAGE_BUDGET_BYTES) {
        refused = new Error(`addin storage full: ${addinId} may hold ${ADDIN_STORAGE_BUDGET_BYTES} bytes`)
        transaction.abort()
        return
      }
      transaction.objectStore('data').put(value, key)
      sizes.put(bytes, key)
    }
    transaction.oncomplete = () => resolve()
    transaction.onabort = () => reject(refused ?? transaction.error)
    transaction.onerror = () => reject(refused ?? transaction.error)
  })
}

export async function addinStorageRemove(addinId: string, key: string): Promise<void> {
  const database = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const transaction = database.transaction(['data', 'sizes'], 'readwrite')
    transaction.objectStore('data').delete(key)
    transaction.objectStore('sizes').delete(key)
    transaction.oncomplete = () => resolve()
    transaction.onerror = () => reject(transaction.error)
  })
}

export async function addinStorageListKeys(addinId: string): Promise<string[]> {
  const database = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const request = database.transaction('data').objectStore('data').getAllKeys()
    request.onsuccess = () => resolve(request.result as string[])
    request.onerror = () => reject(request.error)
  })
}
