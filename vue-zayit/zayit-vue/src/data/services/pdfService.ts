/**
 * PDF Service - Thin Helper for PDF Operations
 * 
 * Simple helper that delegates all WebView logic to the bridge.
 */

import { webviewBridge } from '@/data/services/webviewBridge'

export interface PdfFileResult {
    fileName: string | null
    dataUrl: string | null
    originalPath?: string | null
}

export class PdfService {
    isAvailable(): boolean {
        return webviewBridge.isAvailable()
    }

    async showFilePicker(): Promise<PdfFileResult> {
        return await webviewBridge.openPdfFilePicker()
    }

    async checkManagerReady(): Promise<boolean> {
        return await webviewBridge.checkPdfManagerReady()
    }

    async initializeManager(): Promise<boolean> {
        return await webviewBridge.initializePdfManager()
    }

    async recreateVirtualUrl(originalPath: string): Promise<string | null> {
        return await webviewBridge.recreateVirtualUrl(originalPath)
    }
}

export const pdfService = new PdfService()