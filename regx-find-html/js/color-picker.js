// Color picker module - Enhanced Word-style implementation
function createColorPicker() {
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
        if (!hexColor || hexColor === 'null') return null;

        const cleanedHex = hexColor.replace('#', '');
        if (cleanedHex.length !== 6) return null;

        // Extract RGB components
        const r = parseInt(cleanedHex.substring(0, 2), 16);
        const g = parseInt(cleanedHex.substring(2, 4), 16);
        const b = parseInt(cleanedHex.substring(4, 6), 16);

        // Convert to BGR format (Word's byte order)
        return (b << 16) | (g << 8) | r;
    }

    // Convert Word decimal to hex (for display purposes)
    function wordDecimalToHex(decimal) {
        if (decimal === null || decimal === undefined) return null;

        // Handle special Word values
        if (decimal === -16777216) return '#000000'; // Auto color
        if (decimal < 0) return resolveThemeColor(decimal); // Theme color

        // Extract BGR components and convert to RGB
        const b = (decimal >> 16) & 0xFF;
        const g = (decimal >> 8) & 0xFF;
        const r = decimal & 0xFF;

        return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`.toUpperCase();
    }

    // Resolve theme color to actual hex value
    // Word uses different prefixes: 0xD for Font.Color, 0xF for Find.TextColor
    // Format: 0x[D|F][ThemeIndex][00][Shade][Tint]
    function resolveThemeColor(decimal) {
        if (decimal < 0) {
            // Convert to unsigned 32-bit for bit manipulation
            const unsigned = decimal >>> 0;

            // Extract components from Word's theme color format
            const themeColorByte = (unsigned >> 24) & 0xFF;
            const shadeByte = (unsigned >> 8) & 0xFF;
            const tintByte = unsigned & 0xFF;

            // Check if this is a theme color (0xD0-0xDF or 0xF0-0xFF range)
            const prefix = themeColorByte & 0xF0;
            if (prefix === 0xD0 || prefix === 0xF0) {
                const wdThemeIndex = themeColorByte & 0x0F;

                // Calculate tint/shade adjustment per Word Articles formulas
                let tintShade = 0;
                const unchanged = 0xFF;

                if (shadeByte !== unchanged) {
                    // Shade (darker): Round(-1 + DarknessByte / 255, 2)
                    tintShade = Math.round((-1 + shadeByte / 255) * 100) / 100;
                }

                if (tintByte !== unchanged) {
                    // Tint (lighter): Round(1 - LightnessByte / 255, 2)
                    tintShade = Math.round((1 - tintByte / 255) * 100) / 100;
                }

                // Find base color for this wdThemeColorIndex
                const baseColor = getBaseThemeColorByWdIndex(wdThemeIndex);
                if (baseColor) {
                    return applyTintShade(baseColor, tintShade);
                }
            }
        }

        // Fallback to hard-coded mapping for base theme colors (0xD prefix for Font.Color)
        const themeColorMap = {
            '-587137025': '#000000', // wdThemeColorText1 (13 -> 0xDD)
            '-603914241': '#FFFFFF', // wdThemeColorBackground1 (12 -> 0xDC)
            '-553582593': '#44546A', // wdThemeColorText2 (15 -> 0xDF)
            '-570359809': '#E7E6E6', // wdThemeColorBackground2 (14 -> 0xDE)
            '-738131969': '#4472C4', // wdThemeColorAccent1 (4 -> 0xD4)
            '-721354753': '#ED7D31', // wdThemeColorAccent2 (5 -> 0xD5)
            '-704577537': '#A5A5A5', // wdThemeColorAccent3 (6 -> 0xD6)
            '-687800321': '#FFC000', // wdThemeColorAccent4 (7 -> 0xD7)
            '-671023105': '#5B9BD5', // wdThemeColorAccent5 (8 -> 0xD8)
            '-654245889': '#70AD47'  // wdThemeColorAccent6 (9 -> 0xD9)
        };

        return themeColorMap[decimal.toString()] || '#000000';
    }

    // Get base theme color by Word's wdThemeColorIndex (the lower nibble of theme byte)
    function getBaseThemeColorByWdIndex(wdThemeIndex) {
        // Map wdThemeColorIndex to our themeColors.base array index
        const wdIndexToArrayIndex = {
            5: 4,   // wdThemeColorAccent2 -> Orange
            6: 3,   // wdThemeColorAccent3 -> Gray
            7: 2,   // wdThemeColorAccent4 -> Gold
            8: 1,   // wdThemeColorAccent5 -> Light Blue
            9: 0,   // wdThemeColorAccent6 -> Green
            12: 7,  // wdThemeColorBackground1 -> White
            13: 6,  // wdThemeColorText1 -> Black
            15: 5   // wdThemeColorText2 -> Blue-Gray
        };
        
        const arrayIndex = wdIndexToArrayIndex[wdThemeIndex];
        if (arrayIndex !== undefined) {
            return themeColors.base[arrayIndex]?.hex;
        }
        return null;
    }

    // Decode Word theme color decimal to components (for debugging/analysis)
    // Handles both 0xD prefix (Font.Color) and 0xF prefix (Find.TextColor)
    function decodeThemeColor(decimal) {
        if (decimal >= 0) return null; // Not a theme color

        // Convert to unsigned 32-bit for bit manipulation
        const unsigned = decimal >>> 0;

        // Extract components
        const themeColorByte = (unsigned >> 24) & 0xFF;
        const shadeByte = (unsigned >> 8) & 0xFF;
        const tintByte = unsigned & 0xFF;

        // Check if this is a theme color (0xD0-0xDF or 0xF0-0xFF range)
        const prefix = themeColorByte & 0xF0;
        if (prefix !== 0xD0 && prefix !== 0xF0) return null;

        const wdThemeIndex = themeColorByte & 0x0F;

        // Calculate tint/shade per Word Articles formulas
        let tintShade = 0;
        const unchanged = 0xFF;

        if (shadeByte !== unchanged) {
            // Shade: Round(-1 + DarknessByte / 255, 2)
            tintShade = Math.round((-1 + shadeByte / 255) * 100) / 100;
        }

        if (tintByte !== unchanged) {
            // Tint: Round(1 - LightnessByte / 255, 2)
            tintShade = Math.round((1 - tintByte / 255) * 100) / 100;
        }

        return {
            isThemeColor: true,
            wdThemeIndex: wdThemeIndex,
            tintShade: tintShade,
            prefix: prefix === 0xF0 ? 'F' : 'D',
            rawBytes: {
                themeColorByte: themeColorByte.toString(16).toUpperCase(),
                shadeByte: shadeByte.toString(16).toUpperCase(),
                tintByte: tintByte.toString(16).toUpperCase()
            }
        };
    }

    // RGB to HSL conversion for tint/shade calculations
    function rgbToHsl(r, g, b) {
        r /= 255;
        g /= 255;
        b /= 255;

        const max = Math.max(r, g, b);
        const min = Math.min(r, g, b);
        let h, s, l = (max + min) / 2;

        if (max === min) {
            h = s = 0; // achromatic
        } else {
            const d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            switch (max) {
                case r: h = (g - b) / d + (g < b ? 6 : 0); break;
                case g: h = (b - r) / d + 2; break;
                case b: h = (r - g) / d + 4; break;
            }
            h /= 6;
        }

        return [h, s, l];
    }

    // HSL to RGB conversion
    function hslToRgb(h, s, l) {
        let r, g, b;

        if (s === 0) {
            r = g = b = l; // achromatic
        } else {
            const hue2rgb = (p, q, t) => {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1/6) return p + (q - p) * 6 * t;
                if (t < 1/2) return q;
                if (t < 2/3) return p + (q - p) * (2/3 - t) * 6;
                return p;
            };

            const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            const p = 2 * l - q;
            r = hue2rgb(p, q, h + 1/3);
            g = hue2rgb(p, q, h);
            b = hue2rgb(p, q, h - 1/3);
        }

        return [Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)];
    }

    // Apply tint/shade to a color using Word's formula from Word Articles
    // VBA formula: L = (L * Abs(TintAndShade)) + (Abs(TintAndShade > 0) * (1 - TintAndShade))
    // Tint (positive, e.g. 0.4): newL = L * 0.4 + 0.6 (40% of color + 60% white)
    // Shade (negative, e.g. -0.4): newL = L * 0.4 (40% of color toward black)
    function applyTintShade(hexColor, tintShade) {
        if (tintShade === 0) return hexColor;
        
        const cleanedHex = hexColor.replace('#', '');
        const r = parseInt(cleanedHex.substring(0, 2), 16);
        const g = parseInt(cleanedHex.substring(2, 4), 16);
        const b = parseInt(cleanedHex.substring(4, 6), 16);

        const [h, s, l] = rgbToHsl(r, g, b);

        // Apply Word Articles formula:
        // L = (L * Abs(TintAndShade)) + (isTint * (1 - TintAndShade))
        const absTS = Math.abs(tintShade);
        const isTint = tintShade > 0 ? 1 : 0;
        let newL = (l * absTS) + (isTint * (1 - tintShade));

        newL = Math.max(0, Math.min(1, newL));

        const [newR, newG, newB] = hslToRgb(h, s, newL);

        return `#${newR.toString(16).padStart(2, '0')}${newG.toString(16).padStart(2, '0')}${newB.toString(16).padStart(2, '0')}`.toUpperCase();
    }

    // Word theme index to byte mapping (reverse-engineered from actual Word values)
    // Format: 0x[WordByte][00][Shade][Tint] as signed 32-bit
    const themeIndexToWordByte = [0xDD, 0xDC, 0xDF, 0xDE, 0xD4, 0xD5, 0xD9, 0xD7, 0xD8, 0xD6];

    // Generate theme color with tint/shade decimal value for Word
    function generateThemeColorDecimal(themeIndex, tintShade) {
        if (themeIndex < 0 || themeIndex > 9) return null;

        let shadeValue = 0xFF; // 255 = unchanged
        let tintValue = 0xFF;  // 255 = unchanged

        if (tintShade < 0) {
            // Shade (darker)
            shadeValue = Math.round((tintShade + 1) * 255);
            shadeValue = Math.max(0, Math.min(255, shadeValue));
        } else if (tintShade > 0) {
            // Tint (lighter)
            tintValue = Math.round((1 - tintShade) * 255);
            tintValue = Math.max(0, Math.min(255, tintValue));
        }

        // Use Word's actual byte mapping for theme index
        const wordByte = themeIndexToWordByte[themeIndex];
        const result = (wordByte << 24) | (0x00 << 16) | (shadeValue << 8) | tintValue;

        // Convert to signed 32-bit integer (as Word expects)
        return result > 0x7FFFFFFF ? result - 0x100000000 : result;
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

    // Add color to recent colors
    function addToRecentColors(color, colorData) {
        if (!color || colorData.type === 'auto' || colorData.type === 'none') return;

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
        // Use the webview bridge to send color selection command
        if (window.createWebViewBridge) {
            const bridge = window.createWebViewBridge();
            bridge.handleColorSelection(colorData, context);
        } else {
            console.warn('WebViewBridge not available. Color change would be sent to C# with:', {
                context: context,
                colorData: colorData
            });
        }
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
