/**
 * Hebrew Books Handlers - Unified Event Handlers
 * 
 * Handles Hebrew Books events and tab lifecycle using the unified webviewHebrewBooks service.
 * Provides cache management and cleanup functionality.
 */

import { useTabStore } from '../stores/tabStore'
import { webviewHebrewBooks } from './webviewHebrewBooks'

// Track active Hebrew book files for cleanup
const activeHebrewBookFiles = new Set<string>()

// Global handlers for Hebrew Books events
declare global {
    interface Window {
        handleHebrewBookDownloadComplete?: (notification: any) => void
        handleHebrewBookError?: (notification: any) => void
    }
}

/**
 * Initialize Hebrew Books handlers
 * Sets up global event handlers for C# communication
 */
export function initializeHebrewBooksHandlers() {
    console.log('[HebrewBooksHandlers] Initializing Hebrew books handlers with unified service')

    // Handler for when Hebrew book download completes successfully
    window.handleHebrewBookDownloadComplete = (notification: any) => {
        try {
            console.log('[HebrewBooksHandlers] Hebrew book download complete:', notification)

            const tabStore = useTabStore()
            const activeTab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'hebrewbooks-view')

            if (activeTab && notification.fileName && notification.url) {
                // Set PDF state for the Hebrew book
                activeTab.pdfState = {
                    fileName: notification.fileName,
                    fileUrl: notification.url,
                    source: 'hebrewbook'
                }

                // Track the file for cleanup
                activeHebrewBookFiles.add(notification.fileName)
                console.log('[HebrewBooksHandlers] Updated tab PDF state for Hebrew book')
            } else {
                console.warn('[HebrewBooksHandlers] No active Hebrew books view tab found or missing notification data')
            }
        } catch (error) {
            console.error('[HebrewBooksHandlers] Error handling Hebrew book download complete:', error)
        }
    }

    // Handler for Hebrew book errors
    window.handleHebrewBookError = (notification: any) => {
        try {
            console.error('[HebrewBooksHandlers] Hebrew book error:', notification)
            // Could show toast notification here
        } catch (error) {
            console.error('[HebrewBooksHandlers] Error handling Hebrew book error:', error)
        }
    }

    console.log('[HebrewBooksHandlers] Hebrew books handlers initialized successfully')
}

/**
 * Handle tab closure - notify C# for cache cleanup
 */
export function handleHebrewBookTabClosed(tab: any) {
    try {
        if (tab.pdfState?.source === 'hebrewbook' && tab.pdfState.fileName) {
            const fileName = tab.pdfState.fileName

            console.log('[HebrewBooksHandlers] Hebrew book tab closed, notifying C# for cleanup:', fileName)

            // Remove from our tracking
            activeHebrewBookFiles.delete(fileName)

            // Notify C# for cleanup
            if (webviewHebrewBooks.isAvailable()) {
                webviewHebrewBooks.notifyTabClosed(fileName).catch(error => {
                    console.error('[HebrewBooksHandlers] Error notifying C# of tab closure:', error)
                })
            }
        }
    } catch (error) {
        console.error('[HebrewBooksHandlers] Error handling tab closure:', error)
    }
}

/**
 * Handle tab page change - cleanup when switching away from Hebrew book
 */
export function handleHebrewBookPageChange(tab: any, newPage: string) {
    try {
        if (tab.pdfState?.source === 'hebrewbook' &&
            tab.pdfState.fileName &&
            newPage !== 'hebrewbooks-view') {

            const fileName = tab.pdfState.fileName
            console.log('[HebrewBooksHandlers] Switching away from Hebrew book, cleaning up:', fileName)

            // Remove from our tracking
            activeHebrewBookFiles.delete(fileName)

            // Notify C# for cleanup
            if (webviewHebrewBooks.isAvailable()) {
                webviewHebrewBooks.notifyTabClosed(fileName).catch(error => {
                    console.error('[HebrewBooksHandlers] Error notifying C# of page change:', error)
                })
            }
        }
    } catch (error) {
        console.error('[HebrewBooksHandlers] Error handling tab page change:', error)
    }
}

/**
 * Get Hebrew books cache statistics
 */
export async function getHebrewBooksCacheStats() {
    try {
        if (webviewHebrewBooks.isAvailable()) {
            return await webviewHebrewBooks.getCacheStats()
        }
        return { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
    } catch (error) {
        console.error('[HebrewBooksHandlers] Error getting cache stats:', error)
        return { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
    }
}

/**
 * Clear Hebrew books cache
 */
export async function clearHebrewBooksCache() {
    try {
        if (webviewHebrewBooks.isAvailable()) {
            await webviewHebrewBooks.clearCache()
            activeHebrewBookFiles.clear()
            console.log('[HebrewBooksHandlers] Hebrew books cache cleared')
        }
    } catch (error) {
        console.error('[HebrewBooksHandlers] Error clearing cache:', error)
    }
}

// Auto-initialize when module is imported
initializeHebrewBooksHandlers()