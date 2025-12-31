// Button elements
let showReplaceButton = document.getElementById('show-replace-button');
let themeToggleButton = document.getElementById('theme-toggle-button');
let helpToggleButton = document.getElementById('help-toggle-button');
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
let findFontSizeInput = document.getElementById('find-font-size-input');
let replaceFontSizeInput = document.getElementById('replace-font-size-input');

// Search combobox elements (these are custom elements, not regular inputs)
let findTextInput = document.getElementById('find-text-input');
let replaceTextInput = document.getElementById('replace-text-input');

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

// Send JSON directly to C# using webview bridge with proper command structure
function sendToCS(command, data) {
    if (webViewBridge) {
        // Use the refactored bridge methods
        switch(command) {
            case 'Search':
                webViewBridge.search();
                break;
            case 'Replace':
                webViewBridge.replace();
                break;
            case 'ReplaceAll':
                webViewBridge.replaceAll();
                break;
            case 'GetFontList':
                webViewBridge.getFontList();
                break;
            case 'GetStyleList':
                webViewBridge.getStyleList();
                break;
            case 'CopyFormatting':
                webViewBridge.copyFormatting(data.target);
                break;
            default:
                console.warn('Unknown command:', command);
        }
    } else {
        console.warn('WebView bridge not available. Command:', command);
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
        
        // Add current search to history
        const findInput = document.getElementById('find-text-input');
        if (findInput?.addCurrentToHistory) {
            findInput.addCurrentToHistory();
        }
        
        // Hide regex palette and show search results
        if (regexPalette && regexPalette.hideForSearch) {
            regexPalette.hideForSearch();
        }
        
        sendToCS('Search');
        
        // Focus the search box after search operation
        setTimeout(() => {
            if (findInput?.shadowRoot) {
                findInput.shadowRoot.querySelector('.search-input')?.focus();
            } else {
                findInput?.focus();
            }
        }, 50);
    });

    // Replace button
    replaceButton.addEventListener('click', (e) => {
        e.preventDefault();
        
        // Add current replace text to history
        const replaceInput = document.getElementById('replace-text-input');
        if (replaceInput?.addCurrentToHistory) {
            replaceInput.addCurrentToHistory();
        }
        
        sendToCS('Replace');
    });

    // Replace all button
    replaceAllButton.addEventListener('click', (e) => {
        e.preventDefault();
        
        // Add current replace text to history
        const replaceInput = document.getElementById('replace-text-input');
        if (replaceInput?.addCurrentToHistory) {
            replaceInput.addCurrentToHistory();
        }
        
        sendToCS('ReplaceAll');
    });

    // Theme toggle
    themeToggleButton.addEventListener('click', (e) => {
        e.preventDefault();
        document.documentElement.classList.toggle('dark');
        // Theme toggle is purely UI - no need to send to C#
    });

    // Help toggle (regex palette)
    if (helpToggleButton) {
        helpToggleButton.addEventListener('click', (e) => {
            e.preventDefault();
            console.log('Help button clicked, regexPalette:', regexPalette);
            if (regexPalette) {
                regexPalette.toggle();
            } else {
                console.error('regexPalette is not initialized');
            }
        });
    }

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

        findFontSizeInput.value = '';
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

        replaceFontSizeInput.value = '';
        replaceStyleInput.setValue('');
        replaceFontInput.setValue('');
        // No need to send to C# - this is pure UI operation
    });

    // Copy formatting buttons (dipper/eyedropper functionality)
    findDipperButton.addEventListener('click', (e) => {
        e.preventDefault();
        if (webViewBridge) {
            webViewBridge.copyFormatting('find');
        }
    });

    replaceOptionsDipperButton.addEventListener('click', (e) => {
        e.preventDefault();
        if (webViewBridge) {
            webViewBridge.copyFormatting('replace');
        }
    });

    // Enter key support for search comboboxes
    const findTextInput = document.getElementById('find-text-input');
    const replaceTextInput = document.getElementById('replace-text-input');
    
    if (findTextInput) {
        findTextInput.addEventListener('enter', (e) => {
            // Hide regex palette and show search results
            if (regexPalette && regexPalette.hideForSearch) {
                regexPalette.hideForSearch();
            }
            
            sendToCS('Search');
            
            // Keep focus in search box after search
            setTimeout(() => {
                if (findTextInput.shadowRoot) {
                    findTextInput.shadowRoot.querySelector('.search-input')?.focus();
                }
            }, 50);
        });

        // Escape key support - clear input text
        findTextInput.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !findTextInput.hasAttribute('open')) {
                e.preventDefault();
                findTextInput.clearValue();
                if (findTextInput.shadowRoot) {
                    findTextInput.shadowRoot.querySelector('.search-input')?.focus();
                }
            }
        });
    }

    if (replaceTextInput) {
        replaceTextInput.addEventListener('enter', (e) => {
            sendToCS('Replace');
        });

        // Escape key support - clear input text
        replaceTextInput.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !replaceTextInput.hasAttribute('open')) {
                e.preventDefault();
                replaceTextInput.clearValue();
                if (replaceTextInput.shadowRoot) {
                    replaceTextInput.shadowRoot.querySelector('.search-input')?.focus();
                }
            }
        });
    }

    // Request style list when style inputs are focused (dynamic)
    if (findStyleInput) {
        findStyleInput.addEventListener('focus', () => {
            if (webViewBridge) {
                webViewBridge.getStyleList();
            }
        });
    }

    if (replaceStyleInput) {
        replaceStyleInput.addEventListener('focus', () => {
            if (webViewBridge) {
                webViewBridge.getStyleList();
            }
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
                webViewBridge.getFontList();
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

    // Re-get references to elements after DOM is fully loaded
    findTextInput = document.getElementById('find-text-input');
    replaceTextInput = document.getElementById('replace-text-input');

    // Setup all event listeners
    setupColorPickerEvents();
    setupButtonEvents();
});
