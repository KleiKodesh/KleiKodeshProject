/**
 * PDF Service
 * 
 * Handles PDF file operations using the existing C# bridge system.
 * Integrates with the virtual host mapping PDF manager in C#.
 */

import { CSharpBridge } from '../data/csharpBridge'

export interface PdfFileResult {
    fileName: string | null
    dataUrl: string | null  // This will be the virtual HTTPS URL
    originalPath?: string | null  // Original file path for persistence
}

export class PdfService {
    private bridge: CSharpBridge

    constructor() {
        this.bridge = CSharpBridge.getInstance()
    }

    /**
     * Show C# file picker and get virtual PDF URL
     * Uses the existing bridge system with OpenPdfFilePicker command
     */
    async showFilePicker(): Promise<PdfFileResult> {
        console.log('[PdfService] Opening file picker via C# bridge')

        // Create promise that will be resolved by receivePdfFilePath
        const promise = this.bridge.createRequest<PdfFileResult>('OpenPdfFilePicker')

        // Send command to C# (uses existing bridge pattern)
        console.log('[PdfService] Sending OpenPdfFilePicker command to C#')
        this.bridge.send('OpenPdfFilePicker', [])

        // Wait for C# response via bridge
        console.log('[PdfService] Waiting for C# response...')
        const result = await promise
        
        console.log('[PdfService] Received file picker result:', result)
        return result
    }

    /**
     * Check if PDF manager is ready for operations
     */
    async checkManagerReady(): Promise<boolean> {
        console.log('[PdfService] Checking PDF manager readiness')

        // Create promise that will be resolved by receivePdfManagerReady
        const promise = this.bridge.createRequest<boolean>('CheckPdfManagerReady')

        // Send command to C# 
        this.bridge.send('CheckPdfManagerReady', [])

        // Wait for C# response
        const isReady = await promise
        
        console.log('[PdfService] PDF manager ready:', isReady)
        return isReady
    }

    /**
     * Recreate virtual URL from stored file path (for session persistence)
     */
    async recreateVirtualUrl(originalPath: string): Promise<string | null> {
        console.log('[PdfService] Recreating virtual URL for:', originalPath)

        // Create promise that will be resolved by receivePdfVirtualUrl
        const promise = this.bridge.createRequest<string | null>(`RecreateVirtualUrlFromPath:${originalPath}`)

        // Send command to C# 
        this.bridge.send('RecreateVirtualUrlFromPath', [originalPath])

        // Wait for C# response
        const virtualUrl = await promise
        
        console.log('[PdfService] Recreated virtual URL:', virtualUrl)
        return virtualUrl
    }

    /**
     * Check if C# bridge is available
     */
    isAvailable(): boolean {
        return this.bridge.isAvailable()
    }
}

// Export singleton instance
export const pdfService = new PdfService()