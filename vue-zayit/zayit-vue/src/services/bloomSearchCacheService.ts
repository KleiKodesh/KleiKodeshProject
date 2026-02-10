import type { BloomSearchResult } from '../types/BloomSearch'

interface CachedSearch {
    query: string
    results: BloomSearchResult[]
    timestamp: number
}

class BloomSearchCacheService {
    private dbName = 'ZayitSearchCache'
    private storeName = 'searches'
    private version = 1
    private maxCacheSize = 100
    private db: IDBDatabase | null = null

    async init(): Promise<void> {
        if (this.db) return

        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName, this.version)

            request.onerror = () => {
                console.error('[SearchCache] Failed to open database:', request.error)
                reject(request.error)
            }

            request.onsuccess = () => {
                this.db = request.result
                console.log('[SearchCache] Database opened successfully')
                resolve()
            }

            request.onupgradeneeded = (event) => {
                const db = (event.target as IDBOpenDBRequest).result

                if (!db.objectStoreNames.contains(this.storeName)) {
                    const store = db.createObjectStore(this.storeName, { keyPath: 'query' })
                    store.createIndex('timestamp', 'timestamp', { unique: false })
                    console.log('[SearchCache] Object store created')
                }
            }
        })
    }

    async get(query: string): Promise<BloomSearchResult[] | null> {
        await this.init()
        if (!this.db) return null

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readonly')
            const store = transaction.objectStore(this.storeName)
            const request = store.get(query)

            request.onsuccess = () => {
                const cached = request.result as CachedSearch | undefined
                if (cached) {
                    console.log('[SearchCache] Cache hit for query:', query)
                    // Update timestamp to refresh LRU position
                    this.updateTimestamp(query).catch(err =>
                        console.error('[SearchCache] Failed to update timestamp:', err)
                    )
                    resolve(cached.results)
                } else {
                    console.log('[SearchCache] Cache miss for query:', query)
                    resolve(null)
                }
            }

            request.onerror = () => {
                console.error('[SearchCache] Failed to get from cache:', request.error)
                reject(request.error)
            }
        })
    }

    async set(query: string, results: BloomSearchResult[]): Promise<void> {
        await this.init()
        if (!this.db) return

        // Check cache size and evict if needed
        await this.evictIfNeeded()

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readwrite')
            const store = transaction.objectStore(this.storeName)

            const cached: CachedSearch = {
                query,
                results,
                timestamp: Date.now()
            }

            const request = store.put(cached)

            request.onsuccess = () => {
                console.log('[SearchCache] Cached results for query:', query)
                resolve()
            }

            request.onerror = () => {
                console.error('[SearchCache] Failed to cache results:', request.error)
                reject(request.error)
            }
        })
    }

    private async updateTimestamp(query: string): Promise<void> {
        if (!this.db) return

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readwrite')
            const store = transaction.objectStore(this.storeName)
            const getRequest = store.get(query)

            getRequest.onsuccess = () => {
                const cached = getRequest.result as CachedSearch
                if (cached) {
                    cached.timestamp = Date.now()
                    const putRequest = store.put(cached)

                    putRequest.onsuccess = () => resolve()
                    putRequest.onerror = () => reject(putRequest.error)
                } else {
                    resolve()
                }
            }

            getRequest.onerror = () => reject(getRequest.error)
        })
    }

    private async evictIfNeeded(): Promise<void> {
        if (!this.db) return

        const count = await this.getCount()

        if (count >= this.maxCacheSize) {
            console.log('[SearchCache] Cache full, evicting oldest entry')
            await this.evictOldest()
        }
    }

    private async getCount(): Promise<number> {
        if (!this.db) return 0

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readonly')
            const store = transaction.objectStore(this.storeName)
            const request = store.count()

            request.onsuccess = () => resolve(request.result)
            request.onerror = () => reject(request.error)
        })
    }

    private async evictOldest(): Promise<void> {
        if (!this.db) return

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readwrite')
            const store = transaction.objectStore(this.storeName)
            const index = store.index('timestamp')
            const request = index.openCursor()

            request.onsuccess = () => {
                const cursor = request.result
                if (cursor) {
                    const cached = cursor.value as CachedSearch
                    console.log('[SearchCache] Evicting oldest query:', cached.query)
                    cursor.delete()
                    resolve()
                } else {
                    resolve()
                }
            }

            request.onerror = () => reject(request.error)
        })
    }

    async clear(): Promise<void> {
        await this.init()
        if (!this.db) return

        return new Promise((resolve, reject) => {
            const transaction = this.db!.transaction([this.storeName], 'readwrite')
            const store = transaction.objectStore(this.storeName)
            const request = store.clear()

            request.onsuccess = () => {
                console.log('[SearchCache] Cache cleared')
                resolve()
            }

            request.onerror = () => {
                console.error('[SearchCache] Failed to clear cache:', request.error)
                reject(request.error)
            }
        })
    }

    async getStats(): Promise<{ count: number; maxSize: number }> {
        const count = await this.getCount()
        return {
            count,
            maxSize: this.maxCacheSize
        }
    }
}

export const bloomSearchCacheService = new BloomSearchCacheService()
