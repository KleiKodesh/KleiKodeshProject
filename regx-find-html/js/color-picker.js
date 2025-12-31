// Color picker module - Enhanced Word-style implementation
function createColorPicker() {
    // Initialize color calculations module
    const colorCalc = window.createColorCalculations();
    
    // Private state
    const state = {
        currentColorTarget: null,
        colorPickerOverlay: null,
        colorPicker: null,
        recentColors: [] // Store recent colors (max 10)
    };

    const MAX_RECENT_COLORS = 10;
    const RECENT_COLORS_KEY = 'colorPickerRecentColors';

    // Theme Colors - order matches Word's theme color picker (RTL: first in array = rightmost)
    // NOTE: decimals use 0xD prefix to match Word's Font.Color property
    const themeColors = {
        // Base theme colors - 10 colors, ordered so first = rightmost in RTL display
        base: [
            { name: 'White', hex: '#FFFFFF', themeIndex: 1, decimal: -603914241 },
            { name: 'Black', hex: '#000000', themeIndex: 0, decimal: -587137025 },
            { name: 'Light Gray', hex: '#E7E6E6', themeIndex: 3, decimal: -570359809 },
            { name: 'Blue-Gray', hex: '#44546A', themeIndex: 2, decimal: -553582593 },
            { name: 'Blue', hex: '#4472C4', themeIndex: 4, decimal: -738131969 },
            { name: 'Orange', hex: '#ED7D31', themeIndex: 5, decimal: -721354753 },
            { name: 'Gray', hex: '#A5A5A5', themeIndex: 9, decimal: -704577537 },
            { name: 'Gold', hex: '#FFC000', themeIndex: 7, decimal: -687800321 },
            { name: 'Light Blue', hex: '#5B9BD5', themeIndex: 8, decimal: -671023105 },
            { name: 'Green', hex: '#70AD47', themeIndex: 6, decimal: -654245889 }
        ],
        variations: []
    };

    // Standard Colors - single row, ordered so first = rightmost in RTL display
    // Visual order RTL: Dark Red, Red, Orange, Yellow, Light Green, Green, Light Blue, Blue, Dark Blue, Purple
    const standardColors = [
        { name: 'Dark Red', hex: '#C00000', decimal: 192 },
        { name: 'Red', hex: '#FF0000', decimal: 255 },
        { name: 'Orange', hex: '#FFC000', decimal: 49407 },
        { name: 'Yellow', hex: '#FFFF00', decimal: 65535 },
        { name: 'Light Green', hex: '#92D050', decimal: 5296274 },
        { name: 'Green', hex: '#00B050', decimal: 5287936 },
        { name: 'Light Blue', hex: '#00B0F0', decimal: 15773696 },
        { name: 'Blue', hex: '#0070C0', decimal: 12611584 },
        { name: 'Dark Blue', hex: '#002060', decimal: 6299648 },
        { name: 'Purple', hex: '#7030A0', decimal: 10498160 }
    ];

    // Convert hex color to Word decimal format (BGR byte order)
    function hexToWordDecimal(hexColor) {
        return colorCalc.hexToWordDecimal(hexColor);
    }

    // Convert Word decimal to hex (for display purposes)
    function wordDecimalToHex(decimal) {
        return colorCalc.wordDecimalToHex(decimal);
    }

    // Resolve theme color to actual hex value
    // Word uses different prefixes: 0xD for Font.Color, 0xF for Find.TextColor
    // Format: 0x[D|F][ThemeIndex][00][Shade][Tint]
    function resolveThemeColor(decimal) {
        return colorCalc.resolveThemeColor(decimal);
    }

    // Get base theme color by Word's wdThemeColorIndex (the lower nibble of theme byte)
    function getBaseThemeColorByWdIndex(wdThemeIndex) {
        return colorCalc.getBaseThemeColorByWdIndex(wdThemeIndex);
    }

    // Decode Word theme color decimal to components (for debugging/analysis)
    // Handles both 0xD prefix (Font.Color) and 0xF prefix (Find.TextColor)
    function decodeThemeColor(decimal) {
        return colorCalc.decodeThemeColor(decimal);
    }

    // Apply tint/shade to a color using Word's formula from Word Articles
    // VBA formula: L = (L * Abs(TintAndShade)) + (Abs(TintAndShade > 0) * (1 - TintAndShade))
    // Tint (positive, e.g. 0.4): newL = L * 0.4 + 0.6 (40% of color + 60% white)
    // Shade (negative, e.g. -0.4): newL = L * 0.4 (40% of color toward black)
    function applyTintShade(hexColor, tintShade) {
        return colorCalc.applyTintShade(hexColor, tintShade);
    }

    // Generate theme color with tint/shade decimal value for Word
    function generateThemeColorDecimal(themeIndex, tintShade) {
        return colorCalc.generateThemeColorDecimal(themeIndex, tintShade);
    }

    // Initialize decimal values for theme and standard colors
    function initializeColorDecimals() {
        // Initialize theme colors with decimal values (calculation now matches Word)
        themeColors.base.forEach(color => {
            color.decimal = generateThemeColorDecimal(color.themeIndex, 0);
        });

        // Initialize standard colors with decimal values
        standardColors.forEach(color => {
            color.decimal = hexToWordDecimal(color.hex);
        });
    }

    // Initialize color picker
    function initialize() {
        state.colorPickerOverlay = document.getElementById('color-picker-overlay');
        state.colorPicker = document.getElementById('color-picker');

        // Initialize color decimal values
        initializeColorDecimals();

        populateColorGrids();
        setupEventListeners();
    }

    // Populate color grids
    function populateColorGrids() {
        // Populate standard colors (top section)
        const standardColorsGrid = document.getElementById('standard-colors-grid');
        standardColorsGrid.innerHTML = '';

        standardColors.forEach(color => {
            const swatch = createColorSwatch(color.hex, {
                type: 'standard',
                name: color.name,
                decimal: color.decimal
            });
            standardColorsGrid.appendChild(swatch);
        });

        // Populate theme colors (bottom section)
        const themeColorsGrid = document.getElementById('theme-colors-grid');
        themeColorsGrid.innerHTML = '';

        themeColors.base.forEach(color => {
            const swatch = createColorSwatch(color.hex, {
                type: 'theme',
                name: color.name,
                themeIndex: color.themeIndex,
                decimal: color.decimal,
                tintShade: 0
            });
            themeColorsGrid.appendChild(swatch);
        });

        // Setup auto and no-color buttons
        document.querySelector('.auto-color').addEventListener('click', () => {
            selectColor('#000000', {
                type: 'auto',
                name: 'Automatic',
                decimal: -16777216
            });
        });

        document.querySelector('.no-color').addEventListener('click', () => {
            selectColor(null, {
                type: 'none',
                name: 'No Color',
                decimal: null
            });
        });

        // Load and display recent colors
        loadRecentColors();
        updateRecentColorsGrid();
    }

    // Load recent colors from localStorage
    function loadRecentColors() {
        try {
            const stored = localStorage.getItem(RECENT_COLORS_KEY);
            if (stored) {
                state.recentColors = JSON.parse(stored);
            }
        } catch (e) {
            state.recentColors = [];
        }
    }

    // Save recent colors to localStorage
    function saveRecentColors() {
        try {
            localStorage.setItem(RECENT_COLORS_KEY, JSON.stringify(state.recentColors));
        } catch (e) {
            // localStorage not available
        }
    }

    // Add color to recent colors - only for custom colors from native picker
    function addToRecentColors(color, colorData) {
        if (!color || colorData.type === 'auto' || colorData.type === 'none') return;
        
        // Only add custom colors picked through the native color picker
        if (colorData.type !== 'custom') return;

        // Remove if already exists
        state.recentColors = state.recentColors.filter(c => c.hex !== color);

        // Add to beginning
        state.recentColors.unshift({ hex: color, ...colorData });

        // Keep only MAX_RECENT_COLORS
        if (state.recentColors.length > MAX_RECENT_COLORS) {
            state.recentColors = state.recentColors.slice(0, MAX_RECENT_COLORS);
        }

        saveRecentColors();
        updateRecentColorsGrid();
    }

    // Update recent colors grid display
    function updateRecentColorsGrid() {
        const recentSection = document.getElementById('recent-colors-section');
        const recentGrid = document.getElementById('recent-colors-grid');

        if (!recentGrid || !recentSection) return;

        recentGrid.innerHTML = '';

        if (state.recentColors.length === 0) {
            recentSection.style.display = 'none';
            return;
        }

        recentSection.style.display = 'block';

        state.recentColors.forEach(colorData => {
            const swatch = createColorSwatch(colorData.hex, {
                type: colorData.type || 'custom',
                name: colorData.name || 'Recent Color',
                decimal: colorData.decimal
            });
            recentGrid.appendChild(swatch);
        });
    }

    // Create color swatch element
    function createColorSwatch(color, colorData) {
        const swatch = document.createElement('button');
        swatch.className = 'color-swatch';
        swatch.style.backgroundColor = color;
        swatch.setAttribute('data-color', color);
        swatch.setAttribute('data-color-info', JSON.stringify(colorData));
        swatch.title = colorData.name || color;
        swatch.addEventListener('click', () => selectColor(color, colorData));
        return swatch;
    }

    // Setup event listeners
    function setupEventListeners() {
        // Close button
        const colorPickerClose = document.getElementById('color-picker-close');
        colorPickerClose.addEventListener('click', () => hide());

        // Click outside to close
        state.colorPickerOverlay.addEventListener('click', (event) => {
            if (event.target === state.colorPickerOverlay) {
                hide();
            }
        });

        // More colors button - opens native color picker
        const moreColorsBtn = document.getElementById('more-colors-btn');
        moreColorsBtn.addEventListener('click', (event) => {
            event.preventDefault();
            openNativeColorPicker();
        });

        // Escape key to close
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && state.colorPickerOverlay.style.display === 'flex') {
                hide();
            }
        });
    }

    // Show color picker
    function show(targetButton) {
        state.currentColorTarget = targetButton;
        state.colorPickerOverlay.style.display = 'flex';

        // Position the color picker near the button
        const rect = targetButton.getBoundingClientRect();

        // Reset positioning classes
        state.colorPicker.classList.remove('position-top');

        // Check if there's enough space below the button
        if (rect.bottom + 400 > window.innerHeight) {
            state.colorPicker.classList.add('position-top');
        }
    }

    // Hide color picker
    function hide() {
        state.colorPickerOverlay.style.display = 'none';
        state.currentColorTarget = null;
    }

    // Open native system color picker
    function openNativeColorPicker() {
        // Store the current target before hiding the main picker
        const targetButton = state.currentColorTarget;

        // Hide the main color picker
        hide();

        // Create a hidden color input element
        const colorInput = document.createElement('input');
        colorInput.type = 'color';
        colorInput.style.position = 'absolute';
        colorInput.style.left = '-9999px';
        colorInput.style.opacity = '0';

        // Set current color if available
        if (targetButton && targetButton.getAttribute('data-selected-color')) {
            colorInput.value = targetButton.getAttribute('data-selected-color');
        }

        // Add to DOM temporarily
        document.body.appendChild(colorInput);

        // Handle color selection
        colorInput.addEventListener('change', (event) => {
            const selectedColor = event.target.value;
            // Temporarily restore the target for color selection
            state.currentColorTarget = targetButton;
            selectColor(selectedColor, {
                type: 'custom',
                name: 'Custom Color',
                decimal: hexToWordDecimal(selectedColor)
            });
            document.body.removeChild(colorInput);
        });

        // Handle cancel (when user closes without selecting)
        colorInput.addEventListener('blur', () => {
            // Small delay to allow change event to fire first
            setTimeout(() => {
                if (document.body.contains(colorInput)) {
                    document.body.removeChild(colorInput);
                }
            }, 100);
        });

        // Trigger the native color picker
        colorInput.click();
    }

    // Select color - enhanced to handle Word color data
    function selectColor(color, colorData) {
        if (state.currentColorTarget) {
            // Update the button's visual state
            if (color) {
                state.currentColorTarget.style.borderBottom = `3px solid ${color}`;
                state.currentColorTarget.setAttribute('data-selected-color', color);
                state.currentColorTarget.setAttribute('data-color-data', JSON.stringify(colorData));
                state.currentColorTarget.classList.add('active');
            } else {
                state.currentColorTarget.style.borderBottom = '';
                state.currentColorTarget.removeAttribute('data-selected-color');
                state.currentColorTarget.removeAttribute('data-color-data');
                state.currentColorTarget.classList.remove('active');
            }

            // Trigger any additional logic based on the button
            if (state.currentColorTarget.id === 'find-color-button') {
                notifyColorChange('find', colorData);
            } else if (state.currentColorTarget.id === 'replace-color-toggle') {
                notifyColorChange('replace', colorData);
            }
        }

        // Add to recent colors (not for auto/none)
        addToRecentColors(color, colorData);

        // Close the dialog
        hide();
    }

    // Notify C# VSTO about color change - follows webview bridge pattern
    function notifyColorChange(context, colorData) {
        // Color selection is handled purely in the UI - no C# communication needed
        // The color data is stored in the button's data attributes and will be
        // sent to C# when search/replace operations are performed
        console.log('Color selected for', context, ':', colorData);
    }

    // Get current color data for a button (useful for C# to query current state)
    function getCurrentColorData(buttonId) {
        const button = document.getElementById(buttonId);
        if (!button) return null;

        const colorDataStr = button.getAttribute('data-color-data');
        if (!colorDataStr) return null;

        try {
            return JSON.parse(colorDataStr);
        } catch (error) {
            return null;
        }
    }

    // Get all current formatting state (follows webview bridge pattern of collecting all data at once)
    function getAllFormattingState() {
        if (window.createWebViewBridge) {
            const bridge = window.createWebViewBridge();
            return {
                findOptions: bridge.getFindOptions(),
                replaceOptions: bridge.getReplaceOptions(),
                searchParameters: bridge.getSearchParameters()
            };
        }
        return null;
    }

    // Return public interface
    return {
        initialize,
        show,
        hide,
        selectColor,
        getCurrentColorData,
        getAllFormattingState,
        hexToWordDecimal,
        wordDecimalToHex
    };
}

// Export for use in other files
window.createColorPicker = createColorPicker;
