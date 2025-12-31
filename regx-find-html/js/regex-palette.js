// Regex palette functionality
function createRegexPalette() {
    // Private state
    const state = {
        regexTips: [],
        isVisible: false
    };

    // Initialize the regex palette
    async function initialize() {
        // Use bundled data from JavaScript module only
        if (window.regexTipsData) {
            state.regexTips = window.regexTipsData;
            setupEventListeners();
        } else {
            console.error('Regex tips data not available - window.regexTipsData is missing');
        }
    }

    // Setup event listeners
    function setupEventListeners() {
        const helpToggleButton = document.getElementById('help-toggle-button');
        if (helpToggleButton) {
            helpToggleButton.addEventListener('click', (event) => {
                event.preventDefault();
                toggle();
            });
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
        }

        if (helpToggleButton) {
            helpToggleButton.classList.add('active');
        }
    }

    // Hide the palette
    function hide() {
        const regexPalette = document.getElementById('regex-palette');
        const searchResults = document.getElementById('search-results');
        const helpToggleButton = document.getElementById('help-toggle-button');

        if (regexPalette) {
            regexPalette.style.display = 'none';
        }

        // Show search results when hiding regex palette
        if (searchResults) {
            searchResults.style.display = 'flex';
        }

        if (helpToggleButton) {
            helpToggleButton.classList.remove('active');
        }
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
        // Find the currently focused input or default to search input
        const activeElement = document.activeElement;
        let targetInput = null;

        if (activeElement && (activeElement.id === 'find-text-input' || activeElement.id === 'replace-text-input')) {
            targetInput = activeElement;
        } else {
            // Default to search input if no input is focused
            targetInput = document.getElementById('find-text-input');
        }

        if (targetInput) {
            const cursorPos = targetInput.selectionStart;
            const textBefore = targetInput.value.substring(0, cursorPos);
            const textAfter = targetInput.value.substring(targetInput.selectionEnd);

            targetInput.value = textBefore + symbol + textAfter;
            targetInput.focus();
            targetInput.setSelectionRange(cursorPos + symbol.length, cursorPos + symbol.length);
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
