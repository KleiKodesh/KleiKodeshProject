// Streamlined WebView Bridge for C# Communication
function createWebViewBridge() {
    // Convert Word decimal to hex (for display purposes) - standalone version
    function wordDecimalToHex(decimal) {
        if (decimal === null || decimal === undefined) return null;

        // Handle special Word values
        if (decimal === -16777216) return '#000000'; // Auto color
        if (decimal < 0) {
            // For theme colors, use a basic fallback for now
            // This is a simplified version - full theme resolution would need document context
            return '#000000'; // Fallback for theme colors
        }

        // Extract BGR components and convert to RGB
        const b = (decimal >> 16) & 0xFF;
        const g = (decimal >> 8) & 0xFF;
        const r = decimal & 0xFF;

        return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`.toUpperCase();
    }
    // Send command to C# backend
    function sendCommand(command, data = null) {
        const message = {
            Command: command,
            Args: data !== null ? [data] : []
        };

        if (window.chrome?.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        } else {
            console.warn('WebView2 API not available. Command:', command);
        }
    }

    // Utility: Get three-state value (true/false/null)
    function getThreeState(elementId) {
        const element = document.getElementById(elementId);
        if (!element) return null;
        
        if (element.classList.contains('toggled-true')) return true;
        if (element.classList.contains('toggled-false')) return false;
        return null; // Default state
    }

    // Collect search parameters from UI
    function getSearchParameters() {
        const findInput = document.getElementById('find-text-input');
        const replaceInput = document.getElementById('replace-text-input');
        
        return {
            searchText: findInput?.getValue ? findInput.getValue() : (findInput?.value || ''),
            replaceText: replaceInput?.getValue ? replaceInput.getValue() : (replaceInput?.value || ''),
            searchMode: document.getElementById('search-mode-select')?.value || 'All',
            slop: parseInt(document.getElementById('slop-input')?.value) || 0,
            useRegex: document.getElementById('regex-toggle-button')?.classList.contains('active') || false,
            findOptions: getFormattingOptions('find'),
            replaceOptions: getFormattingOptions('replace')
        };
    }

    // Get formatting options for find or replace
    function getFormattingOptions(type) {
        const prefix = type === 'find' ? 'find' : 'replace';
        const colorElement = type === 'find' ? 'find-color-button' : 'replace-color-toggle';
        
        // Get style and font from custom comboboxes (use getValue() method)
        const styleInput = document.getElementById(`${prefix}-style-input`);
        const fontInput = document.getElementById(`${prefix}-font-input`);
        
        // Get color decimal value from data-color-data attribute (contains {decimal: ...})
        let textColor = null;
        const colorButton = document.getElementById(colorElement);
        if (colorButton?.dataset.colorData) {
            try {
                const colorData = JSON.parse(colorButton.dataset.colorData);
                textColor = colorData.decimal ?? null;
            } catch (e) {
                textColor = null;
            }
        }
        
        return {
            fontSize: parseInt(document.getElementById(`${prefix}-font-size-input`)?.value) || null,
            bold: getThreeState(`${prefix}-bold-toggle`),
            italic: getThreeState(`${prefix}-italic-toggle`),
            underline: getThreeState(`${prefix}-underline-toggle`),
            subscript: getThreeState(`${prefix}-subscript-toggle`),
            superscript: getThreeState(`${prefix}-superscript-toggle`),
            textColor: textColor,
            style: styleInput?.getValue ? styleInput.getValue() : (styleInput?.value || ''),
            font: fontInput?.getValue ? fontInput.getValue() : (fontInput?.value || '')
        };
    }

    // Core command handlers
    const commands = {
        search: () => sendCommand('Search', getSearchParameters()),
        replace: () => sendCommand('Replace', getSearchParameters()),
        replaceAll: () => sendCommand('ReplaceAll', getSearchParameters()),
        selectResult: (index) => sendCommand('SelectResult', { Index: index }),
        getFontList: () => sendCommand('GetFontList'),
        getStyleList: () => sendCommand('GetStyleList'),
        copyFormatting: (target) => sendCommand('CopyFormatting', { target })
    };

    // Message listener setup
    function setupMessageListener() {
        if (window.chrome?.webview) {
            window.chrome.webview.addEventListener('message', (event) => {
                handleMessageFromCSharp(JSON.parse(event.data));
            });
        }
    }

    // Handle messages from C#
    function handleMessageFromCSharp(message) {
        const handlers = {
            searchResults: displaySearchResults,
            replaceComplete: () => {}, // No action needed
            formattingCopied: handleFormattingCopied,
            fontList: populateFontLists,
            styleList: populateStyleLists,
            success: () => {}, // No action needed
            error: () => {} // No action needed
        };

        const handler = handlers[message.command];
        if (handler) {
            handler(message.data);
        } else {
            console.warn('Unknown command from C#:', message.command);
        }
    }

    // Display search results (snippets are pre-highlighted by C#)
    function displaySearchResults(results) {
        const resultsContainer = document.getElementById('results-list-content');

        if (results?.length > 0) {
            resultsContainer.innerHTML = results.map((snippet, index) => `
                <div class="result-item" data-index="${index}" tabindex="0">
                    <div class="result-text">${snippet}</div>
                </div>
            `).join('');

            setupResultsNavigation();
        } else {
            resultsContainer.innerHTML = '<div class="no-results">לא נמצאו תוצאות התואמות לחיפוש</div>';
        }
    }

    // Setup results navigation
    function setupResultsNavigation() {
        const resultItems = document.querySelectorAll('.result-item');
        
        resultItems.forEach((item, index) => {
            item.addEventListener('click', () => selectResult(index));
            item.addEventListener('keydown', (event) => {
                const totalResults = resultItems.length;
                switch (event.key) {
                    case 'ArrowDown':
                        event.preventDefault();
                        focusResult(Math.min(index + 1, totalResults - 1));
                        break;
                    case 'ArrowUp':
                        event.preventDefault();
                        focusResult(Math.max(index - 1, 0));
                        break;
                    case 'Enter':
                        event.preventDefault();
                        selectResult(index);
                        break;
                }
            });
        });

        function focusResult(index) {
            resultItems.forEach(item => item.classList.remove('focused'));
            if (resultItems[index]) {
                resultItems[index].classList.add('focused');
                resultItems[index].focus();
                resultItems[index].scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            }
        }

        function selectResult(index) {
            resultItems.forEach(item => item.classList.remove('selected'));
            if (resultItems[index]) {
                resultItems[index].classList.add('selected');
                commands.selectResult(index);
            }
        }
    }

    // Handle formatting copied from selection
    function handleFormattingCopied(data) {
        const target = data.target;
        const formatting = data.formatting;
        applyFormattingToUI(formatting, target);
    }

    // Apply formatting data to UI elements
    function applyFormattingToUI(formatting, target) {
        const prefix = target === 'find' ? 'find' : 'replace';
        
        // Apply three-state formatting
        setThreeStateButton(`${prefix}-bold-toggle`, formatting.bold);
        setThreeStateButton(`${prefix}-italic-toggle`, formatting.italic);
        setThreeStateButton(`${prefix}-underline-toggle`, formatting.underline);
        
        // Handle subscript/superscript mutual exclusion - can't both be true
        if (formatting.superscript === true && formatting.subscript === true) {
            // Shouldn't happen, but prefer superscript if both are true
            setThreeStateButton(`${prefix}-superscript-toggle`, true);
            setThreeStateButton(`${prefix}-subscript-toggle`, null);
        } else {
            setThreeStateButton(`${prefix}-superscript-toggle`, formatting.superscript);
            setThreeStateButton(`${prefix}-subscript-toggle`, formatting.subscript);
        }
        
        // Apply font size
        if (formatting.fontSize) {
            const fontSizeInput = document.getElementById(`${prefix}-font-size-input`);
            if (fontSizeInput) fontSizeInput.value = formatting.fontSize;
        }
        
        // Apply font and style
        if (formatting.font) {
            const fontInput = document.getElementById(`${prefix}-font-input`);
            if (fontInput?.setValue) fontInput.setValue(formatting.font);
        }
        
        if (formatting.style) {
            const styleInput = document.getElementById(`${prefix}-style-input`);
            if (styleInput?.setValue) styleInput.setValue(formatting.style);
        }
        
        // Apply text color
        if (formatting.textColor !== null && formatting.textColor !== undefined) {
            console.log('COLOR DEBUG - applyFormattingToUI:');
            console.log('  formatting.textColor:', formatting.textColor);
            console.log('  typeof formatting.textColor:', typeof formatting.textColor);
            
            const colorButton = document.getElementById(target === 'find' ? 'find-color-button' : 'replace-color-toggle');
            if (colorButton) {
                // Use local conversion function instead of relying on window.colorPicker
                const hexColor = wordDecimalToHex(formatting.textColor) || '#000000';
                console.log('  Converted hexColor:', hexColor);
                console.log('  Using local wordDecimalToHex function');
                
                colorButton.style.borderBottom = `3px solid ${hexColor}`;
                colorButton.setAttribute('data-selected-color', hexColor);
                // CRITICAL: Set data-color-data so getFormattingOptions can read it
                colorButton.setAttribute('data-color-data', JSON.stringify({
                    hex: hexColor,
                    decimal: formatting.textColor,
                    type: formatting.textColor < 0 ? 'theme' : 'standard'
                }));
                colorButton.classList.add('active');
                
                console.log('  Final border style:', colorButton.style.borderBottom);
                console.log('  data-color-data:', colorButton.getAttribute('data-color-data'));
            } else {
                console.log('  Color button not found for target:', target);
            }
        } else {
            console.log('COLOR DEBUG - textColor is null/undefined:', formatting.textColor);
        }
    }
    
    // Set three-state button helper
    function setThreeStateButton(buttonId, value) {
        const button = document.getElementById(buttonId);
        if (!button) return;
        
        button.classList.remove('toggled-true', 'toggled-false');
        if (value === true) {
            button.classList.add('toggled-true');
        } else if (value === false) {
            button.classList.add('toggled-false');
        }
    }

    // Populate font lists using custom comboboxes - async chunked loading
    function populateFontLists(fonts) {
        const findFontCombobox = document.getElementById('find-font-input');
        const replaceFontCombobox = document.getElementById('replace-font-input');

        if (findFontCombobox?.updateOptions && replaceFontCombobox?.updateOptions && fonts) {
            // Load first chunk immediately for instant visibility
            const chunkSize = 20;
            const firstChunk = fonts.slice(0, chunkSize);
            findFontCombobox.updateOptions(firstChunk);
            replaceFontCombobox.updateOptions(firstChunk);
            
            // Load remaining fonts in chunks asynchronously
            if (fonts.length > chunkSize) {
                let loadedFonts = [...firstChunk];
                let currentIndex = chunkSize;
                
                function loadNextChunk() {
                    if (currentIndex >= fonts.length) return;
                    
                    const nextChunk = fonts.slice(currentIndex, currentIndex + chunkSize);
                    loadedFonts = loadedFonts.concat(nextChunk);
                    currentIndex += chunkSize;
                    
                    findFontCombobox.updateOptions(loadedFonts);
                    replaceFontCombobox.updateOptions(loadedFonts);
                    
                    // Continue loading if more fonts remain
                    if (currentIndex < fonts.length) {
                        requestAnimationFrame(loadNextChunk);
                    }
                }
                
                // Start async loading after a brief delay
                requestAnimationFrame(loadNextChunk);
            }
        }
    }

    // Populate style lists using custom comboboxes
    function populateStyleLists(styles) {
        const findStyleCombobox = document.getElementById('find-style-input');
        const replaceStyleCombobox = document.getElementById('replace-style-input');

        if (findStyleCombobox?.updateOptions && replaceStyleCombobox?.updateOptions && styles) {
            findStyleCombobox.updateOptions(styles);
            replaceStyleCombobox.updateOptions(styles);
        }
    }

    // Public interface
    return {
        ...commands,
        getSearchParameters,
        getFormattingOptions,
        setupMessageListener
    };
}

// Export for use in other modules
window.createWebViewBridge = createWebViewBridge;
