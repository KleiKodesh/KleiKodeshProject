/**
 * Per-addin sandboxed storage backing the addin `storage.*` API.
 *
 * Each addin gets its own IndexedDB database keyed by its addin id (the plugin
 * folder name). Addins can only ever touch their own database — this grants no
 * access to any app data, which is why `storage.*` stays enabled under the
 * data-query-only policy.
 */

function openAddinDatabase(addinId: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(`app-addin-storage-${addinId}`, 1)
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains('data'))
        request.result.createObjectStore('data')
    }
    request.onsuccess = () => resolve(request.result)
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
  return new Promise((resolve, reject) => {
    const request = database.transaction('data', 'readwrite').objectStore('data').put(value, key)
    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error)
  })
}

export async function addinStorageRemove(addinId: string, key: string): Promise<void> {
  const database = await openAddinDatabase(addinId)
  return new Promise((resolve, reject) => {
    const request = database.transaction('data', 'readwrite').objectStore('data').delete(key)
    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error)
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
