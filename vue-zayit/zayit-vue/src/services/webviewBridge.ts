/**
 * WebView Bridge - Singleton Communication System
 * 
 * Clean, simple bridge for C# WebView2 communication.
 * Uses lazy loading and singleton pattern with global message listener.
 * Contains all WebView logic - other services are just thin helpers.
 */

interface BridgeMessage {
    id: string
    method: string
    params: any[]
}

interface BridgeResponse {
    id: string
    result?: any
    error?: string
}

type PendingRequest = {
    resolve: (value: any) => void
    reject: (error: Error) => void
}

class WebViewBridge {
    private static instance: WebViewBridge | null = null
    private pendingRequests = new Map<string, PendingRequest>()
    private requestCounter = 0
    private messageListenerSetup = false
    private handlersInitialized = false

    private constructor() {
        // Lazy setup - only initialize when first used
    }

    static getInstance(): WebViewBridge {
        if (!WebViewBridge.instance) {
            WebViewBridge.instance = new WebViewBridge()
        }
        return WebViewBridge.instance
    }

    isAvailable(): boolean {
        return typeof window !== 'undefined' &&
            (window as any).chrome?.webview !== undefined
    }

    private setupMessageListener(): void {
        if (this.messageListenerSetup || typeof window === 'undefined') return
        this.messageListenerSetup = true

        console.log('[WebViewBridge] Setting up lazy message listener')

            // Global message listener for all C# responses
            ; (window as any).handleBridgeResponse = (response: BridgeResponse) => {
                console.log('[WebViewBridge] Received response:', response.id)

                if (!response || !response.id) {
                    console.warn('[WebViewBridge] Invalid response format:', response)
                    return
                }

                const pending = this.pendingRequests.get(response.id)
                if (pending) {
                    this.pendingRequests.delete(response.id)

                    if (response.error) {
                        pending.reject(new Error(response.error))
                    } else {
                        pending.resolve(response.result)
                    }
                } else {
                    console.warn('[WebViewBridge] No pending request for:', response.id)
                }
            }

        // Listen for all messages from C# (lazy loaded)
        if (this.isAvailable()) {
            (window as any).chrome.webview.addEventListener('message', (event: any) => {
                try {
                    if (!event || !event.data) {
                        console.warn('[WebViewBridge] Received empty event or data')
                        return
                    }

                    const message = JSON.parse(event.data)

                    if (!message) {
                        console.warn('[WebViewBridge] Parsed message is null/undefined')
                        return
                    }

                    // Check if this is a search streaming message
                    if (message.type && message.searchId) {
                        this.handleSearchMessage(message)
                        return
                    }

                    if (message.id && (message.result !== undefined || message.error !== undefined)) {
                        // This is a response to our request
                        ; (window as any).handleBridgeResponse(message)
                    } else {
                        // This might be an unsolicited message from C#
                        console.log('[WebViewBridge] Received unsolicited message:', message)
                    }
                } catch (error) {
                    console.error('[WebViewBridge] Error parsing message:', error, 'Raw data:', event?.data)
                }
            })

            console.log('[WebViewBridge] Message listener registered successfully')
        } else {
            console.warn('[WebViewBridge] WebView not available for message listener setup')
        }
    }

    private searchListeners = new Map<string, {
        onBatch: (results: any[]) => void
        onComplete: () => void
        onCancelled: () => void
        onError: (error: string) => void
    }>()

    private handleSearchMessage(message: any): void {
        const { type, searchId } = message
        const listener = this.searchListeners.get(searchId)

        if (!listener) {
            // Silently ignore messages for unregistered searches (they were cancelled)
            return
        }

        switch (type) {
            case 'searchBatch':
                console.log('[WebViewBridge] Received search batch:', searchId, message.results?.length)
                listener.onBatch(message.results || [])
                break
            case 'searchComplete':
                console.log('[WebViewBridge] Search complete:', searchId)
                listener.onComplete()
                this.searchListeners.delete(searchId)
                break
            case 'searchCancelled':
                console.log('[WebViewBridge] Search cancelled:', searchId)
                listener.onCancelled()
                this.searchListeners.delete(searchId)
                break
            case 'searchError':
                console.log('[WebViewBridge] Search error:', searchId, message.error)
                listener.onError(message.error || 'Unknown error')
                this.searchListeners.delete(searchId)
                break
        }
    }

    registerSearchListener(
        searchId: string,
        onBatch: (results: any[]) => void,
        onComplete: () => void,
        onCancelled: () => void,
        onError: (error: string) => void
    ): void {
        // Remove any existing listener for this searchId first (shouldn't happen, but be safe)
        if (this.searchListeners.has(searchId)) {
            console.warn('[WebViewBridge] Replacing existing listener for search:', searchId)
            this.searchListeners.delete(searchId)
        }

        this.searchListeners.set(searchId, { onBatch, onComplete, onCancelled, onError })
        console.log('[WebViewBridge] Registered listener for search:', searchId)
    }

    unregisterSearchListener(searchId: string): void {
        const existed = this.searchListeners.delete(searchId)
        if (existed) {
            console.log('[WebViewBridge] Unregistered listener for search:', searchId)
        }
    }

    private async initializeHandlers(): Promise<void> {
        if (this.handlersInitialized || !this.isAvailable()) return
        this.handlersInitialized = true

        console.log('[WebViewBridge] Initializing Hebrew books handlers')

        // Import and initialize Hebrew books handlers
        try {
            const { initializeHebrewBooksHandlers } = await import('./hebrewBooksHandlers')
            initializeHebrewBooksHandlers()
            console.log('[WebViewBridge] Hebrew books handlers initialized successfully')
        } catch (error) {
            console.error('[WebViewBridge] Failed to initialize Hebrew books handlers:', error)
        }
    }

    async call<T = any>(method: string, ...params: any[]): Promise<T> {
        if (!method || typeof method !== 'string' || method.trim() === '') {
            throw new Error('Method name is required and must be a non-empty string')
        }

        if (!this.isAvailable()) {
            throw new Error('WebView bridge not available')
        }

        // Setup message listener and handlers on first call (lazy loading)
        this.setupMessageListener()
        await this.initializeHandlers()

        const id = `req_${++this.requestCounter}`
        const message: BridgeMessage = {
            id,
            method: method.trim(),
            params: params || []
        }

        console.log(`[WebViewBridge] Calling ${method} with ID ${id}`)

        return new Promise<T>((resolve, reject) => {
            // Set timeout to prevent hanging requests
            const timeoutId = setTimeout(() => {
                this.pendingRequests.delete(id)
                reject(new Error(`Request timeout for method: ${method}`))
            }, 30000) // 30 second timeout

            this.pendingRequests.set(id, {
                resolve: (value: any) => {
                    clearTimeout(timeoutId)
                    resolve(value)
                },
                reject: (error: Error) => {
                    clearTimeout(timeoutId)
                    reject(error)
                }
            })

            try {
                // Send message to C# as JSON string
                const messageJson = JSON.stringify(message)
                console.log(`[WebViewBridge] Sending message: ${messageJson}`)
                    ; (window as any).chrome.webview.postMessage(messageJson)
            } catch (error) {
                // Clean up on send failure
                clearTimeout(timeoutId)
                this.pendingRequests.delete(id)
                reject(new Error(`Failed to send message: ${error}`))
            }
        })
    }

    // Hebrew Books WebView Methods

    // Flow 1: Open book for viewing (cache if needed, no SaveAs dialog)
    async prepareHebrewBookForViewing(bookId: string, title: string): Promise<{ success: boolean; cached?: boolean; fileName?: string; url?: string }> {
        console.log('[WebViewBridge] Preparing Hebrew book for viewing:', bookId, title)
        try {
            const result = await this.call<{ success: boolean; cached?: boolean; fileName?: string; url?: string }>('PrepareHebrewBookForViewing', bookId, title)
            console.log('[WebViewBridge] Hebrew book viewing prepare result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to prepare Hebrew book for viewing:', error)
            return { success: false }
        }
    }

    // Flow 2: Download book with SaveAs dialog (user chooses location)
    async prepareHebrewBookForDownload(bookId: string, title: string): Promise<{ success: boolean; cancelled?: boolean; filePath?: string }> {
        console.log('[WebViewBridge] Preparing Hebrew book for download with SaveAs dialog:', bookId, title)
        try {
            const result = await this.call<{ success: boolean; cancelled?: boolean; filePath?: string }>('PrepareHebrewBookForDownload', bookId, title)
            console.log('[WebViewBridge] Hebrew book download prepare result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to prepare Hebrew book for download:', error)
            return { success: false }
        }
    }

    // Check if Hebrew book file exists in cache
    async checkHebrewBookInCache(bookId: string, title: string): Promise<{ exists: boolean; fileName?: string; url?: string }> {
        console.log('[WebViewBridge] Checking Hebrew book in cache:', bookId, title)
        try {
            const result = await this.call<{ exists: boolean; fileName?: string; url?: string }>('CheckHebrewBookInCache', bookId, title)
            console.log('[WebViewBridge] Hebrew book cache check result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to check Hebrew book in cache:', error)
            return { exists: false }
        }
    }

    async notifyHebrewBookTabClosed(fileName: string): Promise<void> {
        try {
            await this.call('HandleHebrewBookTabClosed', fileName)
            console.log('[WebViewBridge] Notified C# of Hebrew book tab closure:', fileName)
        } catch (error) {
            console.error('[WebViewBridge] Failed to notify Hebrew book tab closure:', error)
        }
    }

    async getHebrewBooksCacheStats(): Promise<{ totalFiles: number; activeFiles: number; totalSizeMB: number; maxFiles: number }> {
        try {
            const stats = await this.call<{ totalFiles: number; activeFiles: number; totalSizeMB: number; maxFiles: number }>('GetHebrewBooksCacheStats')
            return stats || { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
        } catch (error) {
            console.error('[WebViewBridge] Failed to get Hebrew books cache stats:', error)
            return { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
        }
    }

    async clearHebrewBooksCache(): Promise<void> {
        try {
            await this.call('ClearHebrewBooksCache')
            console.log('[WebViewBridge] Hebrew books cache cleared')
        } catch (error) {
            console.error('[WebViewBridge] Failed to clear Hebrew books cache:', error)
        }
    }

    // PDF WebView Methods
    async openPdfFilePicker(): Promise<{ fileName: string | null; dataUrl: string | null; originalPath?: string | null }> {
        console.log('[WebViewBridge] Opening PDF file picker')
        try {
            const result = await this.call<{ fileName: string | null; dataUrl: string | null; originalPath?: string | null }>('OpenPdfFilePicker')
            console.log('[WebViewBridge] PDF file picker result:', result)
            return result || { fileName: null, dataUrl: null, originalPath: null }
        } catch (error) {
            console.error('[WebViewBridge] PDF file picker failed:', error)
            return { fileName: null, dataUrl: null, originalPath: null }
        }
    }

    async checkPdfManagerReady(): Promise<boolean> {
        console.log('[WebViewBridge] Checking PDF manager readiness')
        try {
            const isReady = await this.call<boolean>('CheckPdfManagerReady')
            console.log('[WebViewBridge] PDF manager ready:', isReady)
            return isReady
        } catch (error) {
            console.error('[WebViewBridge] PDF manager check failed:', error)
            return false
        }
    }

    async initializePdfManager(): Promise<boolean> {
        console.log('[WebViewBridge] Initializing PDF manager')
        try {
            const result = await this.call<boolean>('InitializePdfManager')
            console.log('[WebViewBridge] PDF manager initialized:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] PDF manager initialization failed:', error)
            return false
        }
    }

    async recreateVirtualUrl(originalPath: string): Promise<string | null> {
        console.log('[WebViewBridge] Recreating virtual URL for:', originalPath)
        try {
            const virtualUrl = await this.call<string | null>('RecreateVirtualUrlFromPath', originalPath)
            console.log('[WebViewBridge] Recreated virtual URL:', virtualUrl)
            return virtualUrl
        } catch (error) {
            console.error('[WebViewBridge] Virtual URL recreation failed:', error)
            return null
        }
    }

    // Database WebView Methods
    async openDatabaseFilePicker(): Promise<{ filePath: string | null; fileName: string | null }> {
        console.log('[WebViewBridge] Opening database file picker')
        try {
            const result = await this.call<{ filePath: string | null; fileName: string | null }>('OpenDatabaseFilePicker')
            console.log('[WebViewBridge] Database file picker result:', result)
            return result || { filePath: null, fileName: null }
        } catch (error) {
            console.error('[WebViewBridge] Database file picker failed:', error)
            return { filePath: null, fileName: null }
        }
    }

    async setDatabasePath(path: string): Promise<boolean> {
        console.log('[WebViewBridge] Setting database path:', path)
        try {
            const result = await this.call<boolean>('SetDatabasePath', path)
            console.log('[WebViewBridge] Database path set result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to set database path:', error)
            return false
        }
    }

    async getCurrentDatabasePath(): Promise<string> {
        console.log('[WebViewBridge] Getting current database path')
        try {
            const result = await this.call<string>('GetCurrentDatabasePath')
            console.log('[WebViewBridge] Current database path:', result)
            return result || ''
        } catch (error) {
            console.error('[WebViewBridge] Failed to get current database path:', error)
            return ''
        }
    }

    async clearDatabasePath(): Promise<boolean> {
        console.log('[WebViewBridge] Clearing database path (reverting to default)')
        try {
            const result = await this.call<boolean>('ClearDatabasePath')
            console.log('[WebViewBridge] Database path cleared result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to clear database path:', error)
            return false
        }
    }

    async validateDatabasePath(path: string): Promise<boolean> {
        console.log('[WebViewBridge] Validating database path:', path)
        try {
            const result = await this.call<boolean>('ValidateDatabasePath', path)
            console.log('[WebViewBridge] Database path validation result:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to validate database path:', error)
            return false
        }
    }

    // Bloom Search WebView Methods
    async isBloomSearchReady(): Promise<boolean> {
        console.log('[WebViewBridge] Checking if Bloom search is ready')
        try {
            const result = await this.call<boolean>('IsBloomSearchReady')
            console.log('[WebViewBridge] Bloom search ready:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to check Bloom search ready:', error)
            return false
        }
    }

    async getBloomIndexingProgress(): Promise<{ isReady: boolean; isIndexing: boolean; processedChunks?: number; totalChunks?: number; percentage?: number; eta?: string }> {
        console.log('[WebViewBridge] Getting Bloom indexing progress')
        try {
            const result = await this.call<{ isReady: boolean; isIndexing: boolean; processedChunks?: number; totalChunks?: number; percentage?: number; eta?: string }>('GetBloomIndexingProgress')
            console.log('[WebViewBridge] Bloom indexing progress:', result)
            return result
        } catch (error) {
            console.error('[WebViewBridge] Failed to get Bloom indexing progress:', error)
            return { isReady: false, isIndexing: false }
        }
    }

    async bloomSearchStart(query: string): Promise<string> {
        console.log('[WebViewBridge] Starting Bloom search:', query)
        try {
            const searchId = await this.call<string>('BloomSearchStart', query)
            console.log('[WebViewBridge] Bloom search started with ID:', searchId)
            return searchId
        } catch (error) {
            console.error('[WebViewBridge] Bloom search start failed:', error)
            throw error
        }
    }

    async bloomSearchCancel(searchId: string): Promise<void> {
        console.log('[WebViewBridge] Cancelling Bloom search:', searchId)
        try {
            await this.call('BloomSearchCancel', searchId)
            console.log('[WebViewBridge] Bloom search cancelled:', searchId)
        } catch (error) {
            console.error('[WebViewBridge] Bloom search cancel failed:', error)
        }
    }

    async bloomSearch(query: string): Promise<Array<{ lineId: number; bookId: number; bookTitle: string; tocText: string; score: number; proximityScore: number; snippet: string }>> {
        console.log('[WebViewBridge] Executing Bloom search (legacy):', query)
        try {
            const results = await this.call<Array<{ lineId: number; bookId: number; bookTitle: string; tocText: string; score: number; proximityScore: number; snippet: string }>>('BloomSearch', query)
            console.log('[WebViewBridge] Bloom search results:', results?.length ?? 0)
            return results || []
        } catch (error) {
            console.error('[WebViewBridge] Bloom search failed:', error)
            return []
        }
    }

    async getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number }> {
        console.log('[WebViewBridge] Getting line index from line ID:', lineId)
        try {
            const result = await this.call<{ lineIndex: number; bookId: number }>('GetLineIndexFromLineId', lineId)
            console.log('[WebViewBridge] Line index result:', result)
            return result || { lineIndex: -1, bookId: -1 }
        } catch (error) {
            console.error('[WebViewBridge] Failed to get line index:', error)
            return { lineIndex: -1, bookId: -1 }
        }
    }
}

// Export singleton instance
export const webviewBridge = WebViewBridge.getInstance()