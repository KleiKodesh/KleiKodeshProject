/**
 * Internet Connectivity Detection Utility
 * 
 * Provides methods to check internet connectivity and monitor changes.
 * Uses multiple detection methods for reliability.
 */

import { ref, readonly } from 'vue'

// Reactive connectivity state
const isOnline = ref(navigator.onLine)

/**
 * Check if internet connection is available
 * Uses multiple methods for better reliability
 */
export async function checkConnectivity(): Promise<boolean> {
    // First check: navigator.onLine (basic browser API)
    if (!navigator.onLine) {
        return false
    }

    // Second check: Try to fetch a small resource with timeout
    try {
        // Use AbortController for timeout
        const controller = new AbortController()
        const timeoutId = setTimeout(() => controller.abort(), 3000) // Reduced timeout

        const response = await fetch('https://www.google.com/favicon.ico', {
            method: 'HEAD',
            mode: 'no-cors',
            cache: 'no-cache',
            signal: controller.signal
        })

        clearTimeout(timeoutId)
        return true
    } catch {
        // If fetch fails, try alternative method
        try {
            // Alternative: try to create an image element (works without CORS)
            return await new Promise<boolean>((resolve) => {
                const img = new Image()
                const timeout = setTimeout(() => {
                    resolve(false)
                }, 3000) // Reduced timeout

                img.onload = () => {
                    clearTimeout(timeout)
                    resolve(true)
                }

                img.onerror = () => {
                    clearTimeout(timeout)
                    resolve(false)
                }

                // Use a small, reliable image
                img.src = 'https://www.google.com/favicon.ico?' + Date.now()
            })
        } catch {
            return false
        }
    }
}

/**
 * Update connectivity state
 */
async function updateConnectivity() {
    const connected = await checkConnectivity()
    isOnline.value = connected
}

/**
 * Initialize connectivity monitoring
 */
export function initConnectivityMonitoring() {
    // Listen to browser online/offline events
    window.addEventListener('online', updateConnectivity)
    window.addEventListener('offline', updateConnectivity)

    // Initial check
    updateConnectivity()

    // Periodic check every 30 seconds when online
    setInterval(() => {
        if (navigator.onLine) {
            updateConnectivity()
        }
    }, 30000)
}

/**
 * Cleanup connectivity monitoring
 */
export function cleanupConnectivityMonitoring() {
    window.removeEventListener('online', updateConnectivity)
    window.removeEventListener('offline', updateConnectivity)
}

/**
 * Get reactive connectivity state
 */
export function useConnectivity() {
    return {
        isOnline: readonly(isOnline),
        checkConnectivity,
        updateConnectivity
    }
}