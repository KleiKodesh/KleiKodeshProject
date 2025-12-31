// WebView Bridge for C# Communication
function createWebViewBridge() {
    // Private state
    const state = {
        vscode: null
    };

    // Initialize the bridge
    function initialize() {
        // For WebView2 environments, only use chrome.webview
        if (typeof window.chrome !== 'undefined' && window.chrome.webview) {
            state.vscode = window.chrome.webview;
        }
        // Note: Removed acquireVsCodeApi check since this is WebView2, not VS Code
    }

    // Send command to C# backend
    function sendCommand(command, data = null) {
        // Format for KleiKodeshWebView: { Command: string, Args: array }
        const message = {
            Command: command,
            Args: data !== null ? [data] : [] // Empty array if no data needed
        };

        // Only use WebView2 API - always send JSON strings
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        } else {
            console.warn('WebView2 API not available. Command would be:', JSON.stringify(message, null, 2));
        }
    }

    // Collect search parameters from the UI
    function getSearchParameters() {
        return {
            searchText: document.getElementById('find-text-input')?.value || '',
            replaceText: document.getElementById('replace-text-input')?.value || '',
            searchMode: document.getElementById('search-mode-select')?.value || 'All',
            slop: parseInt(document.getElementById('slop-input')?.value) || 0,
            useRegex: document.getElementById('regex-toggle-button')?.classList.contains('active') || false,
            findOptions: getFindOptions(),
            replaceOptions: getReplaceOptions()
        };
    }

    // Get find formatting options
    function getFindOptions() {
        return {
            fontSize: parseInt(document.getElementById('find-font-size-input')?.value) || 12,
            bold: document.getElementById('find-bold-toggle')?.classList.contains('active') || false,
            italic: document.getElementById('find-italic-toggle')?.classList.contains('active') || false,
            underline: document.getElementById('find-underline-toggle')?.classList.contains('active') || false,
            subscript: document.getElementById('find-subscript-toggle')?.classList.contains('active') || false,
            superscript: document.getElementById('find-superscript-toggle')?.classList.contains('active') || false,
            color: document.getElementById('find-color-button')?.dataset.selectedColor || null,
            style: document.getElementById('find-style-input')?.value || '',
            font: document.getElementById('find-font-input')?.value || ''
        };
    }

    // Get replace formatting options
    function getReplaceOptions() {
        return {
            fontSize: parseInt(document.getElementById('replace-font-size-input')?.value) || 12,
            bold: document.getElementById('replace-bold-toggle')?.classList.contains('active') || false,
            italic: document.getElementById('replace-italic-toggle')?.classList.contains('active') || false,
            underline: document.getElementById('replace-underline-toggle')?.classList.contains('active') || false,
            subscript: document.getElementById('replace-subscript-toggle')?.classList.contains('active') || false,
            superscript: document.getElementById('replace-superscript-toggle')?.classList.contains('active') || false,
            color: document.getElementById('replace-color-toggle')?.dataset.selectedColor || null,
            style: document.getElementById('replace-style-input')?.value || '',
            font: document.getElementById('replace-font-input')?.value || ''
        };
    }

    // Handle search command
    function handleSearch() {
        const searchParams = getSearchParameters();
        sendCommand('search', searchParams);
    }

    // Handle replace command
    function handleReplace() {
        const searchParams = getSearchParameters();
        sendCommand('replace', searchParams);
    }

    // Handle replace all command
    function handleReplaceAll() {
        const searchParams = getSearchParameters();
        sendCommand('replaceAll', searchParams);
    }

    // Handle result selection
    function handleSelectResult(index) {
        sendCommand('selectResult', { index: index });
    }

    // Handle navigation commands
    function handlePrevResult() {
        sendCommand('PrevResult'); // No data needed
    }

    function handleNextResult() {
        sendCommand('NextResult'); // No data needed
    }

    function handleSelectInDoc() {
        sendCommand('SelectInDoc'); // No data needed
    }

    // Handle theme toggle
    function handleThemeToggle() {
        const isDark = document.documentElement.classList.contains('dark');
        sendCommand('themeToggle', { theme: isDark ? 'light' : 'dark' });
    }

    // Handle color selection
    function handleColorSelection(color, target) {
        sendCommand('colorSelected', {
            color: color,
            target: target // 'find' or 'replace'
        });
    }

    // Handle format option toggle
    function handleFormatToggle(option, target, isActive) {
        sendCommand('formatToggle', {
            option: option, // 'bold', 'italic', 'underline', etc.
            target: target, // 'find' or 'replace'
            isActive: isActive
        });
    }

    // Handle clear formatting
    function handleClearFormatting(target) {
        sendCommand('clearFormatting', { target: target });
    }

    // Handle copy formatting
    function handleCopyFormatting(target) {
        sendCommand('copyFormatting', { target: target });
    }

    // Handle font list request
    function handleGetFontList() {
        sendCommand('GetFontList'); // No data needed
    }

    // Handle style list request
    function handleGetStyleList() {
        sendCommand('GetStyleList'); // No data needed
    }

    // Listen for messages from C#
    function setupMessageListener() {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', (event) => {
                // C# always sends JSON strings - always parse
                handleMessageFromCSharp(JSON.parse(event.data));
            });
        } else if (window.addEventListener) {
            window.addEventListener('message', (event) => {
                // Fallback path - also expects JSON strings
                handleMessageFromCSharp(JSON.parse(event.data));
            });
        }
    }

    // Handle messages received from C#
    function handleMessageFromCSharp(message) {
        // Debug output for browser console
        console.log('Received message from C#:', message);
        console.log('Message command:', message.command);
        console.log('Message data:', message.data);
        
        switch (message.command) {
            case 'searchResults':
                console.log('Processing searchResults with', message.data?.matches?.length || 0, 'matches');
                displaySearchResults(message.data);
                break;
            case 'replaceComplete':
                handleReplaceComplete(message.data);
                break;
            case 'success':
                // Success - no status message needed
                break;
            case 'error':
                // Error - no status message needed  
                break;
            case 'fontList':
                populateFontLists(message.data);
                break;
            case 'styleList':
                populateStyleLists(message.data);
                break;
            default:
                console.warn('Unknown command from C#:', message.command);
        }
    }

    // Handle success messages - removed, no longer needed

    // Display search results with highlighting and navigation
    function displaySearchResults(results) {
        const resultsContainer = document.getElementById('results-list-content');

        if (results.matches && results.matches.length > 0) {
            resultsContainer.innerHTML = results.matches.map((match, index) => {
                // Create highlighted text snippet
                const beforeHighlight = escapeHtml(match.Text.substring(0, match.HighlightStart || 0));
                const highlightText = escapeHtml(match.Text.substring(
                    match.HighlightStart || 0,
                    match.HighlightEnd || match.Text.length
                ));
                const afterHighlight = escapeHtml(match.Text.substring(match.HighlightEnd || match.Text.length));

                return `
                    <div class="result-item" data-index="${index}" tabindex="0">
                        <div class="result-text">
                            ${beforeHighlight}<mark class="search-highlight">${highlightText}</mark>${afterHighlight}
                        </div>
                    </div>
                `;
            }).join('');

            // Setup result selection and keyboard navigation
            setupResultsNavigation(results.matches.length);
        } else {
            resultsContainer.innerHTML = '<div class="no-results">לא נמצאו תוצאות התואמות לחיפוש</div>';
        }
    }

    // Setup search results navigation and selection
    function setupResultsNavigation(resultsCount) {
        const resultsContainer = document.getElementById('results-list-content');
        const resultItems = resultsContainer.querySelectorAll('.result-item');
        let currentSelectedIndex = -1;

        // Click handler for result selection
        resultItems.forEach((item, index) => {
            item.addEventListener('click', () => {
                selectResult(index);
            });

            item.addEventListener('keydown', (event) => {
                handleResultKeydown(event, index, resultsCount);
            });
        });

        // Global keyboard navigation for results
        function handleResultKeydown(event, currentIndex, totalResults) {
            switch (event.key) {
                case 'ArrowDown':
                    event.preventDefault();
                    const nextIndex = Math.min(currentIndex + 1, totalResults - 1);
                    focusResult(nextIndex);
                    break;
                case 'ArrowUp':
                    event.preventDefault();
                    const prevIndex = Math.max(currentIndex - 1, 0);
                    focusResult(prevIndex);
                    break;
                case 'Enter':
                    event.preventDefault();
                    selectResult(currentIndex);
                    break;
                case 'Escape':
                    event.preventDefault();
                    clearResultSelection();
                    break;
            }
        }

        // Focus a specific result
        function focusResult(index) {
            const resultItems = document.querySelectorAll('.result-item');
            if (resultItems[index]) {
                // Remove previous selection
                resultItems.forEach(item => item.classList.remove('focused'));

                // Add focus to new item
                resultItems[index].classList.add('focused');
                resultItems[index].focus();

                // Scroll into view
                resultItems[index].scrollIntoView({ block: 'nearest', behavior: 'smooth' });

                currentSelectedIndex = index;
            }
        }

        // Select a result and notify C#
        function selectResult(index) {
            const resultItems = document.querySelectorAll('.result-item');
            if (resultItems[index]) {
                // Update UI selection state
                resultItems.forEach(item => item.classList.remove('selected'));
                resultItems[index].classList.add('selected');

                // Notify C# of selection using correct command and data structure
                sendCommand('SelectResult', { Index: index });

                currentSelectedIndex = index;
            }
        }

        // Clear result selection
        function clearResultSelection() {
            const resultItems = document.querySelectorAll('.result-item');
            resultItems.forEach(item => {
                item.classList.remove('focused', 'selected');
            });
            currentSelectedIndex = -1;
        }

        // Auto-focus first result if available - DISABLED to keep focus on input fields
        // if (resultsCount > 0) {
        //     setTimeout(() => focusResult(0), 100);
        // }
    }
    function handleReplaceComplete(data) {
        // Replace complete - no status message needed
    }

    // Display error message - removed, no longer needed

    // Apply theme
    function applyTheme(theme) {
        if (theme === 'dark') {
            document.documentElement.classList.add('dark');
        } else {
            document.documentElement.classList.remove('dark');
        }
    }

    // Utility function to escape HTML
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Populate font lists (one-time) with preview text using custom comboboxes
    function populateFontLists(fonts) {
        const findFontCombobox = document.getElementById('find-font-input');
        const replaceFontCombobox = document.getElementById('replace-font-input');

        if (findFontCombobox && replaceFontCombobox && fonts) {
            // Update both comboboxes with font options
            findFontCombobox.updateOptions(fonts);
            replaceFontCombobox.updateOptions(fonts);
        }
    }

    // Populate style lists (dynamic) using custom comboboxes
    function populateStyleLists(styles) {
        const findStyleCombobox = document.getElementById('find-style-input');
        const replaceStyleCombobox = document.getElementById('replace-style-input');

        if (findStyleCombobox && replaceStyleCombobox && styles) {
            // Styles are now simple string arrays
            findStyleCombobox.updateOptions(styles);
            replaceStyleCombobox.updateOptions(styles);
        }
    }



    // Initialize on creation
    initialize();

    // Return public interface
    return {
        sendCommand,
        getSearchParameters,
        getFindOptions,
        getReplaceOptions,
        handleSearch,
        handleReplace,
        handleReplaceAll,
        handleSelectResult,
        handlePrevResult,
        handleNextResult,
        handleSelectInDoc,
        handleThemeToggle,
        handleColorSelection,
        handleFormatToggle,
        handleClearFormatting,
        handleCopyFormatting,
        handleGetFontList,
        handleGetStyleList,
        setupMessageListener
    };
}

// Export for use in other modules
window.createWebViewBridge = createWebViewBridge;
