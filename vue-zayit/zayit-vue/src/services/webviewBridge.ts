/**
 * WebView Bridge - Singleton Communication System
 * 
 * Clean, simple bridge for C# WebView2 communication.
 * Uses lazy loading and singleton pattern with global message listener.
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
    private initialized = false
    private messageListenerSetup = false

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

    async call<T = any>(method: string, ...params: any[]): Promise<T> {
        if (!method || typeof method !== 'string' || method.trim() === '') {
            throw new Error('Method name is required and must be a non-empty string')
        }

        if (!this.isAvailable()) {
            throw new Error('WebView bridge not available')
        }

        // Setup message listener on first call (lazy loading)
        this.setupMessageListener()

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
}

// Export singleton instance
export const webviewBridge = WebViewBridge.getInstance()

// Auto-initialize if in WebView environment
if (typeof window !== 'undefined' && (window as any).chrome?.webview) {
    console.log('[WebViewBridge] WebView detected, initializing bridge')
    webviewBridge // This triggers singleton creation
}