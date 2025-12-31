// Font Manager - Handles font detection using Browser Font Access API with C# fallback
function createFontManager() {
    // Private state
    const state = {
        fonts: [],
        isLoaded: false,
        callbacks: []
    };

    // Check if Browser Font Access API is available
    function isFontAccessAPISupported() {
        return 'queryLocalFonts' in window && typeof window.queryLocalFonts === 'function';
    }

    // Get fonts using Browser Font Access API
    async function getFontsFromAPI() {
        try {
            // Request permission if needed
            const permission = await navigator.permissions.query({ name: 'local-fonts' });

            if (permission.state === 'denied') {
                return null;
            }

            // Query local fonts
            const fonts = await window.queryLocalFonts();

            if (fonts && fonts.length > 0) {
                // Extract unique font families
                const fontFamilies = [...new Set(fonts.map(font => font.family))].sort();
                return fontFamilies;
            }

            return null;
        } catch (error) {
            return null;
        }
    }

    // Get fonts from C# backend
    async function getFontsFromCSharp() {
        return new Promise((resolve) => {
            // Setup message listener for C# response
            const handleFontResponse = (event) => {
                if (event.data && event.data.command === 'fontList') {
                    window.removeEventListener('message', handleFontResponse);
                    resolve(event.data.data);
                }
            };

            window.addEventListener('message', handleFontResponse);

            // Send request to C#
            if (window.webViewBridge) {
                window.webViewBridge.getFontList();
            } else if (window.chrome && window.chrome.webview) {
                // Use correct KleiKodeshWebView format - no Args for GetFontList
                window.chrome.webview.postMessage(JSON.stringify({
                    Command: 'GetFontList',
                    Args: []
                }));
            } else {
                // No communication method available - resolve with empty array
                window.removeEventListener('message', handleFontResponse);
                resolve([]);
            }
        });
    }

    // Load fonts with priority: API -> C#
    async function loadFonts() {
        if (state.isLoaded) {
            return state.fonts;
        }

        let fonts = null;

        // Try Browser Font Access API first (Chrome/Edge experimental)
        if (isFontAccessAPISupported()) {
            fonts = await getFontsFromAPI();
        }

        // Fallback to C# if API failed
        if (!fonts || fonts.length === 0) {
            fonts = await getFontsFromCSharp();
        }

        // No final fallback - use empty array if both methods fail
        if (!fonts || fonts.length === 0) {
            fonts = [];
        }

        state.fonts = fonts;
        state.isLoaded = true;

        // Notify callbacks
        state.callbacks.forEach(callback => {
            try {
                callback(fonts);
            } catch (error) {
                console.error('Font callback error:', error);
            }
        });

        return fonts;
    }

    // Get fonts (async)
    async function getFonts() {
        if (state.isLoaded) {
            return state.fonts;
        }
        return await loadFonts();
    }

    // Add callback for when fonts are loaded
    function onFontsLoaded(callback) {
        if (state.isLoaded) {
            callback(state.fonts);
        } else {
            state.callbacks.push(callback);
        }
    }

    // Check if a specific font is available
    function isFontAvailable(fontName) {
        return state.fonts.includes(fontName);
    }

    // Get font suggestions based on input
    function getFontSuggestions(input, maxResults = 10) {
        if (!input || !state.isLoaded) {
            return state.fonts.slice(0, maxResults);
        }

        const searchTerm = input.toLowerCase();
        const matches = state.fonts.filter(font =>
            font.toLowerCase().includes(searchTerm)
        );

        return matches.slice(0, maxResults);
    }

    // Initialize font loading
    function initialize() {
        // Start loading fonts immediately
        loadFonts().catch(error => {
            console.error('Font loading failed:', error);
            state.fonts = [];
            state.isLoaded = true;
        });
    }

    // Public interface
    return {
        initialize,
        getFonts,
        onFontsLoaded,
        isFontAvailable,
        getFontSuggestions,
        isFontAccessAPISupported: isFontAccessAPISupported()
    };
}

// Export for use in other files
window.createFontManager = createFontManager;
