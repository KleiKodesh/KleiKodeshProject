// Button elements
let showReplaceButton = document.getElementById('show-replace-button');
let themeToggleButton = document.getElementById('theme-toggle-button');
let findButton = document.getElementById('find-button');
let regexToggleButton = document.getElementById('regex-toggle-button');
let findBoldToggle = document.getElementById('find-bold-toggle');
let findItalicToggle = document.getElementById('find-italic-toggle');
let findUnderlineToggle = document.getElementById('find-underline-toggle');
let findSubscriptToggle = document.getElementById('find-subscript-toggle');
let findSuperscriptToggle = document.getElementById('find-superscript-toggle');
let findColorButton = document.getElementById('find-color-button');
let findClearButton = document.getElementById('find-clear-button');
let findDipperButton = document.getElementById('find-dipper-button');
let replaceButton = document.getElementById('replace-button');
let replaceAllButton = document.getElementById('replace-all-button');
let replaceBoldToggle = document.getElementById('replace-bold-toggle');
let replaceItalicToggle = document.getElementById('replace-italic-toggle');
let replaceUnderlineToggle = document.getElementById('replace-underline-toggle');
let replaceSubscriptToggle = document.getElementById('replace-subscript-toggle');
let replaceSuperscriptToggle = document.getElementById('replace-superscript-toggle');
let replaceColorToggle = document.getElementById('replace-color-toggle');
let replaceOptionsClearButton = document.getElementById('replace-options-clear-button');
let replaceOptionsDipperButton = document.getElementById('replace-options-dipper-button');

// Select elements
let searchModeSelect = document.getElementById('search-mode-select');

// Input elements
let slopInput = document.getElementById('slop-input');
let findTextInput = document.getElementById('find-text-input');
let findFontSizeInput = document.getElementById('find-font-size-input');
let replaceTextInput = document.getElementById('replace-text-input');
let replaceFontSizeInput = document.getElementById('replace-font-size-input');

// Custom combobox elements
let findStyleInput = document.getElementById('find-style-input');
let findFontInput = document.getElementById('find-font-input');
let replaceStyleInput = document.getElementById('replace-style-input');
let replaceFontInput = document.getElementById('replace-font-input');

// Color picker instance
let colorPicker;

// WebView bridge instance
let webViewBridge;

// Regex palette instance
let regexPalette;

// Font manager instance
let fontManager;

// Font loading state
let fontsRequested = false;

// Generate search JSON data to match RegexFind.cs properties exactly
function generateSearchData() {
    // Helper function to get three-state value
    function getThreeState(element) {
        if (!element) return null;
        
        if (element.classList.contains('toggled-true')) {
            return true;
        } else if (element.classList.contains('toggled-false')) {
            return false;
        } else {
            return null; // Default state - not specified
        }
    }

    return {
        Text: findTextInput.value,
        Bold: getThreeState(findBoldToggle),
        Italic: getThreeState(findItalicToggle),
        Underline: getThreeState(findUnderlineToggle),
        Superscript: getThreeState(findSuperscriptToggle),
        Subscript: getThreeState(findSubscriptToggle),
        TextColor: findColorButton?.dataset.selectedColor ? parseInt(findColorButton.dataset.selectedColor) : null,
        Style: findStyleInput.getValue(),
        Font: findFontInput.getValue(),
        FontSize: parseInt(findFontSizeInput.value) || null,
        Mode: searchModeSelect.value, // "All", "Forward", "Back", "Selection"
        Slop: parseInt(slopInput.value) || 0,
        UseWildcards: regexToggleButton.classList.contains('active'),
        Replace: {
            Text: '', // Empty for search-only operations
            Bold: null,
            Italic: null,
            Underline: null,
            Superscript: null,
            Subscript: null,
            TextColor: null,
            Style: '',
            Font: '',
            FontSize: null
        }
    };
}

// Generate replace JSON data to match RegexFind.cs properties exactly
function generateReplaceData() {
    // Helper function to get three-state value
    function getThreeState(element) {
        if (!element) return null;
        
        if (element.classList.contains('toggled-true')) {
            return true;
        } else if (element.classList.contains('toggled-false')) {
            return false;
        } else {
            return null; // Default state - not specified
        }
    }

    return {
        Text: findTextInput.value,
        Bold: getThreeState(findBoldToggle),
        Italic: getThreeState(findItalicToggle),
        Underline: getThreeState(findUnderlineToggle),
        Superscript: getThreeState(findSuperscriptToggle),
        Subscript: getThreeState(findSubscriptToggle),
        Style: findStyleInput.getValue(),
        Font: findFontInput.getValue(),
        FontSize: parseInt(findFontSizeInput.value) || null,
        Mode: searchModeSelect.value, // "All", "Forward", "Back", "Selection"
        Slop: parseInt(slopInput.value) || 0,
        UseWildcards: regexToggleButton.classList.contains('active'),
        Replace: {
            Text: replaceTextInput.value,
            Bold: getThreeState(replaceBoldToggle),
            Italic: getThreeState(replaceItalicToggle),
            Underline: getThreeState(replaceUnderlineToggle),
            Superscript: getThreeState(replaceSuperscriptToggle),
            Subscript: getThreeState(replaceSubscriptToggle),
            TextColor: replaceColorToggle?.dataset.selectedColor ? parseInt(replaceColorToggle.dataset.selectedColor) : null,
            Style: replaceStyleInput.getValue(),
            Font: replaceFontInput.getValue(),
            FontSize: parseInt(replaceFontSizeInput.value) || null
        }
    };
}

// Send JSON directly to C# using webview bridge with proper command structure
function sendToCS(command, data) {
    if (webViewBridge) {
        webViewBridge.sendCommand(command, data);
    } else {
        // Fallback to direct WebView2 communication - always send JSON strings
        const message = {
            command: command,
            data: data,
            timestamp: new Date().toISOString()
        };
        
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        } else {
            console.warn('WebView2 API not available. Command would be:', JSON.stringify(message, null, 2));
        }
    }
}

// Event listeners for color picker
function setupColorPickerEvents() {
    // Color buttons
    findColorButton.addEventListener('click', (e) => {
        e.preventDefault();
        colorPicker.show(findColorButton);
    });

    replaceColorToggle.addEventListener('click', (e) => {
        e.preventDefault();
        colorPicker.show(replaceColorToggle);
    });

    // Show replace button
    showReplaceButton.addEventListener('click', (e) => {
        e.preventDefault();
        // Toggle replace section visibility
        const replaceContainer = document.getElementById('search-replace-container');
        const isHidden = window.getComputedStyle(replaceContainer).display === 'none';

        if (isHidden) {
            replaceContainer.style.display = 'flex';
            replaceContainer.style.flexDirection = 'column';
            replaceContainer.style.gap = '5px';
            showReplaceButton.classList.add('active');
        } else {
            replaceContainer.style.display = 'none';
            showReplaceButton.classList.remove('active');
        }
    });
}

// Setup all button event listeners
function setupButtonEvents() {
    // Search button
    findButton.addEventListener('click', (e) => {
        e.preventDefault();
        
        // Hide regex palette and show search results
        if (regexPalette && regexPalette.hideForSearch) {
            regexPalette.hideForSearch();
        }
        
        const searchData = generateSearchData();
        sendToCS('Search', searchData);
        
        // Focus the search box after search operation
        setTimeout(() => {
            findTextInput.focus();
        }, 50);
    });

    // Replace button
    replaceButton.addEventListener('click', (e) => {
        e.preventDefault();
        const replaceData = generateReplaceData();
        sendToCS('Replace', replaceData);
    });

    // Replace all button
    replaceAllButton.addEventListener('click', (e) => {
        e.preventDefault();
        const replaceData = generateReplaceData();
        sendToCS('ReplaceAll', replaceData);
    });

    // Theme toggle
    themeToggleButton.addEventListener('click', (e) => {
        e.preventDefault();
        document.documentElement.classList.toggle('dark');
        const isDark = document.documentElement.classList.contains('dark');
        sendToCS('ThemeToggle', { Theme: isDark ? 'dark' : 'light' });
    });

    // Clear formatting buttons
    findClearButton.addEventListener('click', (e) => {
        e.preventDefault();
        // Clear UI - remove all three-state classes
        findBoldToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        findItalicToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        findUnderlineToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        findSubscriptToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        findSuperscriptToggle.classList.remove('active', 'toggled-true', 'toggled-false');

        // Clear color data properly
        findColorButton.removeAttribute('data-selected-color');
        findColorButton.removeAttribute('data-color-data');
        findColorButton.classList.remove('active');
        findColorButton.style.borderBottom = '';

        findFontSizeInput.value = '12';
        findStyleInput.setValue('');
        findFontInput.setValue('');
        // No need to send to C# - this is pure UI operation
    });

    replaceOptionsClearButton.addEventListener('click', (e) => {
        e.preventDefault();
        // Clear UI - remove all three-state classes
        replaceBoldToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        replaceItalicToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        replaceUnderlineToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        replaceSubscriptToggle.classList.remove('active', 'toggled-true', 'toggled-false');
        replaceSuperscriptToggle.classList.remove('active', 'toggled-true', 'toggled-false');

        // Clear color data properly
        replaceColorToggle.removeAttribute('data-selected-color');
        replaceColorToggle.removeAttribute('data-color-data');
        replaceColorToggle.classList.remove('active');
        replaceColorToggle.style.borderBottom = '';

        replaceFontSizeInput.value = '12';
        replaceStyleInput.setValue('');
        replaceFontInput.setValue('');
        // No need to send to C# - this is pure UI operation
    });

    // Copy formatting buttons (dipper/eyedropper functionality)
    findDipperButton.addEventListener('click', (e) => {
        e.preventDefault();
        if (webViewBridge) {
            webViewBridge.handleCopyFormatting('find');
        } else {
            sendToCS('CopyFormatting', { target: 'find' });
        }
    });

    replaceOptionsDipperButton.addEventListener('click', (e) => {
        e.preventDefault();
        if (webViewBridge) {
            webViewBridge.handleCopyFormatting('replace');
        } else {
            sendToCS('CopyFormatting', { target: 'replace' });
        }
    });

    // Enter key support
    findTextInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            
            // Hide regex palette and show search results
            if (regexPalette && regexPalette.hideForSearch) {
                regexPalette.hideForSearch();
            }
            
            const searchData = generateSearchData();
            sendToCS('Search', searchData);
            
            // Keep focus in search box after search
            setTimeout(() => {
                findTextInput.focus();
            }, 50);
        }
    });

    replaceTextInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            const replaceData = generateReplaceData();
            sendToCS('Replace', replaceData);
        }
    });

    // Escape key support - clear input text
    findTextInput.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            e.preventDefault();
            findTextInput.value = '';
            findTextInput.focus();
        }
    });

    replaceTextInput.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            e.preventDefault();
            replaceTextInput.value = '';
            replaceTextInput.focus();
        }
    });

    // Request style list when style inputs are focused (dynamic)
    if (findStyleInput) {
        findStyleInput.addEventListener('focus', () => {
            sendToCS('GetStyleList', {});
        });
    }

    if (replaceStyleInput) {
        replaceStyleInput.addEventListener('focus', () => {
            sendToCS('GetStyleList', {});
        });
    }

}

// Initialize everything when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    // Initialize color picker
    if (typeof createColorPicker !== 'undefined') {
        colorPicker = createColorPicker();
        colorPicker.initialize();
    }

    // Initialize webview bridge
    if (typeof createWebViewBridge !== 'undefined') {
        webViewBridge = createWebViewBridge();
        webViewBridge.setupMessageListener();
        
        // Request font list immediately when page loads (only once)
        if (!fontsRequested) {
            setTimeout(() => {
                console.log('Requesting font list on page load...');
                sendToCS('GetFontList', {});
                fontsRequested = true;
            }, 100);
        }
    }

    // Initialize regex palette
    if (typeof createRegexPalette !== 'undefined') {
        regexPalette = createRegexPalette();
        regexPalette.initialize();
    }

    // Initialize font manager
    if (typeof createFontManager !== 'undefined') {
        fontManager = createFontManager();
        fontManager.initialize();
    }

    // Setup all event listeners
    setupColorPickerEvents();
    setupButtonEvents();
});
