/**
 * C# WebView2 Bridge
 * 
 * Handles communication between Vue frontend and C# backend via WebView2.
 * Uses promise-based request/response pattern.
 */

// Singleton instance
let bridgeInstance: CSharpBridge | null = null;

export class CSharpBridge {
    private pendingRequests = new Map<string, { resolve: Function, reject: Function }>()

    constructor() {
        // Ensure only one instance sets up global handlers
        if (!bridgeInstance) {
            this.setupGlobalHandlers()
            bridgeInstance = this;
        } else {
            // Return existing instance to share pendingRequests
            return bridgeInstance;
        }
    }

    /**
     * Get the singleton instance
     */
    static getInstance(): CSharpBridge {
        if (!bridgeInstance) {
            bridgeInstance = new CSharpBridge();
        }
        return bridgeInstance;
    }

    /**
     * Check if WebView2 is available
     */
    isAvailable(): boolean {
        return typeof window !== 'undefined' &&
            (window as any).chrome?.webview !== undefined
    }

    /**
     * Send command to C#
     */
    send(command: string, args: any[]): void {
        console.log(`[CSharpBridge] Sending command: ${command}`, args)

        if (!this.isAvailable()) {
            console.warn('[CSharpBridge] WebView2 not available, cannot send command:', command)
            return
        }

        (window as any).chrome.webview.postMessage({
            command,
            args
        })

        console.log(`[CSharpBridge] Command sent successfully: ${command}`)
    }

    /**
     * Create a promise that will be resolved when C# responds
     */
    createRequest<T>(requestId: string): Promise<T> {
        console.log(`[CSharpBridge] Creating request promise for: ${requestId}`)

        return new Promise((resolve, reject) => {
            this.pendingRequests.set(requestId, { resolve, reject })
            console.log(`[CSharpBridge] Promise created and stored for: ${requestId}`)
        })
    }

    /**
     * Setup global handlers for C# responses
     */
    private setupGlobalHandlers(): void {
        if (typeof window === 'undefined') return

        const win = window as any

        // Tree data response
        win.receiveTreeData = (data: any) => {
            console.log('[CSharpBridge] receiveTreeData called with data:', data)
            const request = this.pendingRequests.get('GetTree')
            if (request) {
                console.log('[CSharpBridge] Resolving GetTree request')
                request.resolve(data)
                this.pendingRequests.delete('GetTree')
            } else {
                console.warn('[CSharpBridge] No pending request found for GetTree')
                console.log('[CSharpBridge] Current pending requests:', Array.from(this.pendingRequests.keys()))
            }
        }

        // TOC data response
        win.receiveTocData = (bookId: number, data: any) => {
            const request = this.pendingRequests.get(`GetToc:${bookId}`)
            if (request) {
                request.resolve(data)
                this.pendingRequests.delete(`GetToc:${bookId}`)
            }
        }

        // Links response
        win.receiveLinks = (tabId: string, bookId: number, links: any) => {
            const request = this.pendingRequests.get(`GetLinks:${tabId}:${bookId}`)
            if (request) {
                request.resolve(links)
                this.pendingRequests.delete(`GetLinks:${tabId}:${bookId}`)
            }
        }

        // Total lines response
        win.receiveTotalLines = (bookId: number, totalLines: number) => {
            const request = this.pendingRequests.get(`GetTotalLines:${bookId}`)
            if (request) {
                request.resolve(totalLines)
                this.pendingRequests.delete(`GetTotalLines:${bookId}`)
            }
        }

        // Line content response
        win.receiveLineContent = (bookId: number, lineIndex: number, content: string | null) => {
            const request = this.pendingRequests.get(`GetLineContent:${bookId}:${lineIndex}`)
            if (request) {
                request.resolve(content)
                this.pendingRequests.delete(`GetLineContent:${bookId}:${lineIndex}`)
            }
        }

        // Line ID response
        win.receiveLineId = (bookId: number, lineIndex: number, lineId: number | null) => {
            const request = this.pendingRequests.get(`GetLineId:${bookId}:${lineIndex}`)
            if (request) {
                request.resolve(lineId)
                this.pendingRequests.delete(`GetLineId:${bookId}:${lineIndex}`)
            }
        }

        // Line range response
        win.receiveLineRange = (bookId: number, start: number, end: number, lines: any[]) => {
            const request = this.pendingRequests.get(`GetLineRange:${bookId}:${start}:${end}`)
            if (request) {
                request.resolve(lines)
                this.pendingRequests.delete(`GetLineRange:${bookId}:${start}:${end}`)
            }
        }

        // PDF file picker response
        win.receivePdfFilePath = (virtualUrl: string | null, fileName: string | null, originalPath: string | null) => {
            console.log('receivePdfFilePath called:', { virtualUrl, fileName, originalPath });
            console.log('Current pending requests:', Array.from(this.pendingRequests.keys()));
            const request = this.pendingRequests.get('OpenPdfFilePicker')
            if (request) {
                console.log('Resolving PDF picker request');
                // Return both virtual URL (for current session) and original path (for persistence)
                request.resolve({ 
                    fileName, 
                    dataUrl: virtualUrl,  // Virtual URL for PDF.js viewing
                    originalPath         // Original file path for persistence
                })
                this.pendingRequests.delete('OpenPdfFilePicker')
            } else {
                console.log('No pending request found for OpenPdfFilePicker');
                console.log('Available requests:', Array.from(this.pendingRequests.keys()));
            }
        }

        // PDF manager readiness response
        win.receivePdfManagerReady = (isReady: boolean) => {
            const request = this.pendingRequests.get('CheckPdfManagerReady')
            if (request) {
                request.resolve(isReady)
                this.pendingRequests.delete('CheckPdfManagerReady')
            }
        }

        // PDF virtual URL recreation response
        win.receivePdfVirtualUrl = (originalPath: string, virtualUrl: string | null) => {
            const request = this.pendingRequests.get(`RecreateVirtualUrlFromPath:${originalPath}`)
            if (request) {
                request.resolve(virtualUrl)
                this.pendingRequests.delete(`RecreateVirtualUrlFromPath:${originalPath}`)
            }
        }

        // Search lines response
        win.receiveSearchResults = (bookId: number, searchTerm: string, lines: any[]) => {
            const request = this.pendingRequests.get(`SearchLines:${bookId}:${searchTerm}`)
            if (request) {
                request.resolve(lines)
                this.pendingRequests.delete(`SearchLines:${bookId}:${searchTerm}`)
            }
        }

        // Hebrew book download ready response
        win.receiveHebrewBookDownloadReady = (bookId: string, action: string) => {
            console.log(`[CSharpBridge] receiveHebrewBookDownloadReady called - bookId: ${bookId}, action: ${action}`)

            const requestId = `PrepareHebrewBookDownload:${bookId}:${action}`
            const request = this.pendingRequests.get(requestId)

            if (request) {
                console.log(`[CSharpBridge] Resolving request: ${requestId}`)
                request.resolve({ success: true })
                this.pendingRequests.delete(requestId)
            } else {
                console.warn(`[CSharpBridge] No pending request found for: ${requestId}`)
                console.log('[CSharpBridge] Current pending requests:', Array.from(this.pendingRequests.keys()))
            }
        }

        // Hebrew book download complete response (for both viewing and downloading)
        win.receiveHebrewBookDownloadComplete = (bookId: string, result: boolean | string | null) => {
            console.log(`[CSharpBridge] receiveHebrewBookDownloadComplete called - bookId: ${bookId}, result: ${result}`)

            // Handle both view and download completion
            const viewRequestId = `HebrewBookDownloadComplete:${bookId}`
            const downloadRequestId = `PrepareHebrewBookDownload:${bookId}:download`

            const viewRequest = this.pendingRequests.get(viewRequestId)
            const downloadRequest = this.pendingRequests.get(downloadRequestId)

            const request = viewRequest || downloadRequest
            const requestId = viewRequest ? viewRequestId : downloadRequestId

            if (request) {
                console.log(`[CSharpBridge] Resolving download complete request: ${requestId}`)
                
                if (viewRequest && typeof result === 'string' && result !== null) {
                    // For view action - result is sanitized title
                    request.resolve({ success: result })
                } else if (downloadRequest) {
                    // For download action - result is file path or null
                    request.resolve({ success: !!result, filePath: result })
                } else if (typeof result === 'boolean') {
                    // Legacy boolean response
                    request.resolve({ success: result })
                } else {
                    // Error case
                    request.resolve({ success: false })
                }
                this.pendingRequests.delete(requestId)
            } else {
                console.warn(`[CSharpBridge] No pending request found for download complete. Tried: ${viewRequestId}, ${downloadRequestId}`)
                console.log('[CSharpBridge] Current pending requests:', Array.from(this.pendingRequests.keys()))
            }
        }
    }
}