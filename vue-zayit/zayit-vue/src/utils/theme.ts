/**
 * Theme utility for managing dark/light mode
 * Also syncs theme with PDF.js viewer
 */

export function toggleTheme(): void {
    const isDark = document.documentElement.classList.contains('dark')

    if (isDark) {
        document.documentElement.classList.remove('dark')
        localStorage.setItem('theme', 'light')
    } else {
        document.documentElement.classList.add('dark')
        localStorage.setItem('theme', 'dark')
    }
    
    // Sync with PDF.js viewer
    syncPdfViewerTheme()
}

export function initTheme(): void {
    const savedTheme = localStorage.getItem('theme')
    if (savedTheme === 'dark') {
        document.documentElement.classList.add('dark')
    }
    
    // Sync with PDF.js viewer on init
    syncPdfViewerTheme()
    
    // Set up observer to automatically sync theme with new PDF iframes
    setupPdfViewerThemeObserver()
}

export function isDarkTheme(): boolean {
    return document.documentElement.classList.contains('dark')
}

/**
 * Syncs the current Vue app theme with PDF.js viewer iframes
 * PDF.js uses .is-dark class on html element to override system theme
 */
export function syncPdfViewerTheme(): void {
    const isDark = isDarkTheme()
    
    // Find all PDF.js viewer iframes
    const pdfIframes = document.querySelectorAll('iframe[src*="/pdfjs/web/viewer.html"]')
    
    pdfIframes.forEach((iframe) => {
        try {
            const iframeDoc = (iframe as HTMLIFrameElement).contentDocument
            if (iframeDoc?.documentElement) {
                if (isDark) {
                    // Force dark mode in PDF.js
                    iframeDoc.documentElement.classList.add('is-dark')
                    iframeDoc.documentElement.classList.remove('is-light')
                } else {
                    // Force light mode in PDF.js
                    iframeDoc.documentElement.classList.add('is-light')
                    iframeDoc.documentElement.classList.remove('is-dark')
                }
            }
        } catch (error) {
            // Ignore cross-origin errors - iframe might not be loaded yet
            console.debug('[Theme] Could not access PDF iframe:', error)
        }
    })
}

/**
 * Sets up a MutationObserver to automatically sync theme with new PDF iframes
 * This ensures that PDF viewers loaded after theme initialization get the correct theme
 */
function setupPdfViewerThemeObserver(): void {
    // Only set up once
    if ((window as any).__pdfThemeObserverSetup) return
    ;(window as any).__pdfThemeObserverSetup = true
    
    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    const element = node as Element
                    
                    // Check if the added node is a PDF iframe
                    if (element.tagName === 'IFRAME' && 
                        element.getAttribute('src')?.includes('/pdfjs/web/viewer.html')) {
                        // Wait a bit for the iframe to load, then sync theme
                        setTimeout(() => {
                            syncPdfViewerTheme()
                        }, 200)
                    }
                    
                    // Also check for PDF iframes within the added node
                    const pdfIframes = element.querySelectorAll?.('iframe[src*="/pdfjs/web/viewer.html"]')
                    if (pdfIframes?.length) {
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
