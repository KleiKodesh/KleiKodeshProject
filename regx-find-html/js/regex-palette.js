// Regex palette functionality
function createRegexPalette() {
    // Private state
    const state = {
        regexTips: [],
        isVisible: false,
        lastFocusedInput: 'find' // Track which input was last focused: 'find' or 'replace'
    };

    // Initialize the regex palette
    async function initialize() {
        // Use bundled data from JavaScript module only
        if (window.regexTipsData) {
            state.regexTips = window.regexTipsData;
            setupEventListeners();
            setupInputTracking();
        } else {
            console.error('Regex tips data not available - window.regexTipsData is missing');
        }
    }

    // Setup event listeners
    function setupEventListeners() {
        // Event listener is handled by main.js to avoid duplicates
        // This function is kept for potential future use
    }

    // Track which input was last focused
    function setupInputTracking() {
        const findCombobox = document.getElementById('find-text-input');
        const replaceCombobox = document.getElementById('replace-text-input');
        
        // Track focus on find input
        if (findCombobox) {
            const findInput = findCombobox.shadowRoot?.querySelector('.search-input');
            if (findInput) {
                findInput.addEventListener('focus', () => {
                    state.lastFocusedInput = 'find';
                });
            }
            // Also track when combobox itself gets focus
            findCombobox.addEventListener('focus', () => {
                state.lastFocusedInput = 'find';
            }, true);
        }
        
        // Track focus on replace input
        if (replaceCombobox) {
            const replaceInput = replaceCombobox.shadowRoot?.querySelector('.search-input');
            if (replaceInput) {
                replaceInput.addEventListener('focus', () => {
                    state.lastFocusedInput = 'replace';
                });
            }
            // Also track when combobox itself gets focus
            replaceCombobox.addEventListener('focus', () => {
                state.lastFocusedInput = 'replace';
            }, true);
        }
    }

    // Toggle visibility
    function toggle() {
        const regexPalette = document.getElementById('regex-palette');
        if (!regexPalette) return;

        state.isVisible = !state.isVisible;

        if (state.isVisible) {
            show();
        } else {
            hide();
        }
    }

    // Show the palette
    function show() {
        const regexPalette = document.getElementById('regex-palette');
        const searchResults = document.getElementById('search-results');
        const helpToggleButton = document.getElementById('help-toggle-button');

        if (regexPalette) {
            regexPalette.style.display = 'flex';
            console.log('Regex palette display set to flex, computed style:', window.getComputedStyle(regexPalette).display);
            console.log('Regex palette height:', window.getComputedStyle(regexPalette).height);
            populateRegexGrid();
        }

        // Hide search results when showing regex palette
        if (searchResults) {
            searchResults.style.display = 'none';
            console.log('Search results hidden, display:', window.getComputedStyle(searchResults).display);
        }

        if (helpToggleButton) {
            helpToggleButton.classList.add('active');
        }

        state.isVisible = true;
    }

    // Hide the palette
    function hide() {
        const regexPalette = document.getElementById('regex-palette');
        const searchResults = document.getElementById('search-results');
        const helpToggleButton = document.getElementById('help-toggle-button');

        if (regexPalette) {
            regexPalette.style.display = 'none';
            console.log('Regex palette hidden');
        }

        // Show search results when hiding regex palette
        if (searchResults) {
            searchResults.style.display = 'flex';
            console.log('Search results shown, display:', window.getComputedStyle(searchResults).display);
        }

        if (helpToggleButton) {
            helpToggleButton.classList.remove('active');
        }

        state.isVisible = false;
    }

    // Populate the regex grid with tips
    function populateRegexGrid() {
        const regexGrid = document.getElementById('regex-grid');
        if (!regexGrid || state.regexTips.length === 0) {
            console.log('populateRegexGrid failed:', { regexGrid: !!regexGrid, tipsLength: state.regexTips.length });
            return;
        }

        console.log('Populating regex grid with', state.regexTips.length, 'tips');
        regexGrid.innerHTML = '';

        state.regexTips.forEach(tip => {
            const tipElement = document.createElement('div');
            tipElement.className = 'regex-tip';
            tipElement.innerHTML = `
                <div class="regex-symbol-meaning">
                    <span class="regex-symbol">${escapeHtml(tip.Symbol)}</span>
                    <span class="regex-meaning">${escapeHtml(tip.Meaning)}</span>
                </div>
                <div class="regex-example">${escapeHtml(tip.Example)}</div>
            `;

            // Add click handler to insert symbol into focused input
            tipElement.addEventListener('click', () => {
                insertSymbol(tip.Symbol);
            });

            regexGrid.appendChild(tipElement);
        });

        console.log('Regex grid populated, grid height:', window.getComputedStyle(regexGrid).height);
        console.log('Regex grid flex:', window.getComputedStyle(regexGrid).flex);
    }

    // Insert symbol into the currently focused input (search or replace)
    function insertSymbol(symbol) {
        // Find the combobox elements
        const findCombobox = document.getElementById('find-text-input');
        const replaceCombobox = document.getElementById('replace-text-input');
        
        // Get the actual input elements from the shadow DOM
        const findInput = findCombobox?.shadowRoot?.querySelector('.search-input');
        const replaceInput = replaceCombobox?.shadowRoot?.querySelector('.search-input');
        
        // Use the last focused input
        const targetInput = state.lastFocusedInput === 'replace' ? replaceInput : findInput;

        if (targetInput) {
            const cursorPos = targetInput.selectionStart || targetInput.value.length;
            const textBefore = targetInput.value.substring(0, cursorPos);
            const textAfter = targetInput.value.substring(targetInput.selectionEnd || cursorPos);

            targetInput.value = textBefore + symbol + textAfter;
            
            // Set cursor position after the inserted symbol (not selected)
            const newCursorPos = cursorPos + symbol.length;
            
            // Use setTimeout to ensure the focus and selection happen after the click event completes
            setTimeout(() => {
                targetInput.focus();
                targetInput.setSelectionRange(newCursorPos, newCursorPos);
            }, 0);
        }
    }

    // Escape HTML for safe display
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Hide the palette and show search results (called when search is performed)
    function hideForSearch() {
        const regexPalette = document.getElementById('regex-palette');
        const searchResults = document.getElementById('search-results');
        const helpToggleButton = document.getElementById('help-toggle-button');

        if (regexPalette) {
            regexPalette.style.display = 'none';
        }

        if (searchResults) {
            searchResults.style.display = 'flex';
        }

        if (helpToggleButton) {
            helpToggleButton.classList.remove('active');
        }

        // Update internal state
        state.isVisible = false;
    }

    // Return public interface
    return {
        initialize,
        toggle,
        show,
        hide,
        hideForSearch,
        insertSymbol
    };
}

// Export for use in other files
window.createRegexPalette = createRegexPalette;