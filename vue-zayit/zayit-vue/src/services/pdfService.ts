/**
 * PDF Service - Unified PDF Operations
 * 
 * Handles all PDF file operations using C# WebView bridge with SetVirtualHostNameToFolderMapping.
 * Provides session persistence by storing original file paths and recreating virtual URLs.
 * 
 * Pipeline: Vue → webviewBridge → C# ServiceProvider → PdfService → SetVirtualHostNameToFolderMapping
 */

import { webviewBridge } from '../services/webviewBridge'

export interface PdfFileResult {
    fileName: string | null
    dataUrl: string | null  // Virtual HTTPS URL from SetVirtualHostNameToFolderMapping
    originalPath?: string | null  // Original file path for session persistence
}

export class PdfService {
    /**
     * Show C# file picker dialog and get virtual PDF URL
     * Uses SetVirtualHostNameToFolderMapping for secure file access
     */
    async showFilePicker(): Promise<PdfFileResult> {
        console.log('[PdfService] Opening file picker via webview bridge')

        try {
            const result = await webviewBridge.call<PdfFileResult>('OpenPdfFilePicker')
            console.log('[PdfService] Received file picker result:', result)

            if (!result.fileName || !result.dataUrl) {
                return { fileName: null, dataUrl: null, originalPath: null }
            }

            return result
        } catch (error) {
            console.error('[PdfService] File picker failed:', error)
            return { fileName: null, dataUrl: null, originalPath: null }
        }
    }

    /**
     * Check if PDF manager is ready for operations
     */
    async checkManagerReady(): Promise<boolean> {
        console.log('[PdfService] Checking PDF manager readiness')

        try {
            const isReady = await webviewBridge.call<boolean>('CheckPdfManagerReady')
            console.log('[PdfService] PDF manager ready:', isReady)
            return isReady
        } catch (error) {
            console.error('[PdfService] PDF manager check failed:', error)
            return false
        }
    }

    /**
     * Initialize PDF manager with SetVirtualHostNameToFolderMapping
     */
    async initializeManager(): Promise<boolean> {
        console.log('[PdfService] Initializing PDF manager')

        try {
            const result = await webviewBridge.call<boolean>('InitializePdfManager')
            console.log('[PdfService] PDF manager initialized:', result)
            return result
        } catch (error) {
            console.error('[PdfService] PDF manager initialization failed:', error)
            return false
        }
    }

    /**
     * Recreate virtual URL from stored file path (for session persistence)
     * Uses SetVirtualHostNameToFolderMapping to recreate secure access
     */
    async recreateVirtualUrl(originalPath: string): Promise<string | null> {
        console.log('[PdfService] Recreating virtual URL for:', originalPath)

        try {
            const virtualUrl = await webviewBridge.call<string | null>('RecreateVirtualUrlFromPath', originalPath)
            console.log('[PdfService] Recreated virtual URL:', virtualUrl)
            return virtualUrl
        } catch (error) {
            console.error('[PdfService] Virtual URL recreation failed:', error)
            return null
        }
    }

    /**
     * Load PDF from file path (alias for recreateVirtualUrl for compatibility)
     */
    async loadFromPath(originalPath: string): Promise<string | null> {
        return await this.recreateVirtualUrl(originalPath)
    }

    /**
     * Check if webview bridge is available
     */
    isAvailable(): boolean {
        return webviewBridge.isAvailable()
    }
}

// Export singleton instance
export const pdfService = new PdfService()