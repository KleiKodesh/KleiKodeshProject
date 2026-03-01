/**
 * Theme utility for syncing theme with PDF.js viewer
 */

export function initTheme(): void {
    // Sync with PDF.js viewer on init
    syncPdfViewerTheme()

    // Set up observer to automatically sync theme with new PDF iframes
    setupPdfViewerThemeObserver()

    // Make theme functions available globally for debugging
    if (typeof window !== 'undefined') {
        (window as any).zayitTheme = {
            sync: forceSyncAllPdfViewers,
            isDark: isDarkTheme,
            current: () => isDarkTheme() ? 'dark' : 'light'
        }
    }
}

export function isDarkTheme(): boolean {
    return document.documentElement.classList.contains('dark')
}

/**
 * Force sync theme with all PDF.js viewers (useful for debugging)
 */
export function forceSyncAllPdfViewers(): void {
    console.log('[Theme] Force syncing all PDF viewers...')
    syncPdfViewerTheme()

    // Also try to sync after a delay
    setTimeout(() => {
        syncPdfViewerTheme()
    }, 500)
}

/**
 * Syncs the current Vue app theme with PDF.js viewer iframes
 * Uses PDF.js's native theme system via AppOptions.set('viewerCssTheme')
 */
export function syncPdfViewerTheme(): void {
    const isDark = isDarkTheme()

    console.log('[Theme] Syncing PDF viewer theme:', isDark ? 'dark' : 'light')

    // Find all PDF.js viewer iframes
    const pdfIframes = document.querySelectorAll('iframe[src*="/pdfjs/web/viewer.html"]')

    console.log('[Theme] Found PDF iframes:', pdfIframes.length)

    pdfIframes.forEach((iframe, index) => {
        console.log(`[Theme] Processing iframe ${index + 1}:`, iframe.getAttribute('src'))

        try {
            const iframeWindow = (iframe as HTMLIFrameElement).contentWindow
            if (iframeWindow && (iframeWindow as any).PDFViewerApplicationOptions) {
                const AppOptions = (iframeWindow as any).PDFViewerApplicationOptions

                // Set PDF.js theme: 1 = light, 2 = dark
                const themeValue = isDark ? 2 : 1
                AppOptions.set('viewerCssTheme', themeValue)

                console.log(`[Theme] Set PDF.js viewerCssTheme to ${themeValue} (${isDark ? 'dark' : 'light'}) for iframe ${index + 1}`)

                // Also set color-scheme directly like PDF.js does
                const iframeDoc = iframeWindow.document
                if (iframeDoc?.documentElement) {
                    const docStyle = iframeDoc.documentElement.style
                    docStyle.setProperty("color-scheme", isDark ? "dark" : "light")
                    console.log(`[Theme] Set color-scheme to ${isDark ? 'dark' : 'light'} for iframe ${index + 1}`)
                }
            } else {
                console.warn(`[Theme] Iframe ${index + 1} PDFViewerApplicationOptions not available yet`)
            }
        } catch (error) {
            console.warn(`[Theme] Could not access PDF iframe ${index + 1}:`, error)
        }
    })

    if (pdfIframes.length === 0) {
        console.log('[Theme] No PDF iframes found to sync')
    }
}

/**
 * Sets up a MutationObserver to automatically sync theme with new PDF iframes
 * This ensures that PDF viewers loaded after theme initialization get the correct theme
 */
function setupPdfViewerThemeObserver(): void {
    // Only set up once
    if ((window as any).__pdfThemeObserverSetup) return
        ; (window as any).__pdfThemeObserverSetup = true

    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    const element = node as Element

                    // Check if the added node is a PDF iframe
                    if (element.tagName === 'IFRAME' &&
                        element.getAttribute('src')?.includes('/pdfjs/web/viewer.html')) {
                        // Set up load event listener for the iframe
                        const iframe = element as HTMLIFrameElement
                        iframe.addEventListener('load', () => {
                            // Wait a bit more for PDF.js to fully initialize
                            setTimeout(() => {
                                syncPdfViewerTheme()
                            }, 500)
                        })

                        // Also try immediate sync in case iframe is already loaded
                        setTimeout(() => {
                            syncPdfViewerTheme()
                        }, 200)
                    }

                    // Also check for PDF iframes within the added node
                    const pdfIframes = element.querySelectorAll?.('iframe[src*="/pdfjs/web/viewer.html"]')
                    if (pdfIframes?.length) {
                        pdfIframes.forEach((iframe) => {
                            const iframeElement = iframe as HTMLIFrameElement
                            iframeElement.addEventListener('load', () => {
                                setTimeout(() => {
                                    syncPdfViewerTheme()
                                }, 500)
                            })
                        })

                        setTimeout(() => {
                            syncPdfViewerTheme()
                        }, 200)
                    }
                }
            })
        })
    })

    // Observe the entire document for new PDF iframes
    observer.observe(document.body, {
        childList: true,
        subtree: true
    })
}
