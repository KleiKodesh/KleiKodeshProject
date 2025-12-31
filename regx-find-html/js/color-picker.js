// Color picker module - Enhanced Word-style implementation
function createColorPicker() {
    // Private state
    const state = {
        currentColorTarget: null,
        colorPickerOverlay: null,
        colorPicker: null
    };

    // Theme Colors - reversed order with Gray added as 9th (one before last)
    const themeColors = {
        // Base theme colors - 10 colors in the complete reversed order
        base: [
            { name: 'Blue', hex: '#4472C4', themeIndex: 4, decimal: -738131969 },     // 1st - Blue
            { name: 'Blue-Gray', hex: '#44546A', themeIndex: 2, decimal: -553582593 }, // 2nd - Blue-Gray
            { name: 'Light Gray', hex: '#E7E6E6', themeIndex: 3, decimal: -570359809 }, // 3rd - Light Gray
            { name: 'Black', hex: '#000000', themeIndex: 0, decimal: -587137025 },     // 4th - Black
            { name: 'White', hex: '#FFFFFF', themeIndex: 1, decimal: -603914241 },     // 5th - White
            { name: 'Green', hex: '#70AD47', themeIndex: 6, decimal: -654245889 },    // 6th - Green
            { name: 'Light Blue', hex: '#5B9BD5', themeIndex: 8, decimal: -671023105 }, // 7th - Light Blue
            { name: 'Light Orange', hex: '#FFC000', themeIndex: 7, decimal: -687800321 }, // 8th - Light Orange (yellow/golden)
            { name: 'Gray', hex: '#A5A5A5', themeIndex: 9, decimal: -704577537 },    // 9th - Gray (one before last)
            { name: 'Orange', hex: '#ED7D31', themeIndex: 5, decimal: -721354753 }    // 10th - Orange (last)
        ],
        // Remove tint/shade variations to match SimpleColorsDialog (only base colors)
        variations: []
    };

    // Standard Office Colors - exact colors and order from C# dialog image (2x5 grid)
    const standardColors = [
        // Top row (left to right)
        { name: 'Light Green', hex: '#92D050', decimal: null },
        { name: 'Yellow', hex: '#FFFF00', decimal: null },
        { name: 'Orange', hex: '#FFC000', decimal: null },
        { name: 'Red', hex: '#FF0000', decimal: null },
        { name: 'Dark Red', hex: '#C00000', decimal: null },
        // Bottom row (left to right)  
        { name: 'Purple', hex: '#7030A0', decimal: null },
        { name: 'Dark Blue', hex: '#002060', decimal: null },
        { name: 'Blue', hex: '#0070C0', decimal: null },
        { name: 'Light Blue', hex: '#00B0F0', decimal: null },
        { name: 'Green', hex: '#00B050', decimal: null }
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
    function resolveThemeColor(decimal) {
        // Handle negative theme color values by decoding the format
        if (decimal < 0) {
            // Convert to unsigned 32-bit for bit manipulation
            const unsigned = decimal >>> 0;

            // Extract components from Word's theme color format
            const themeColorByte = (unsigned >> 24) & 0xFF;
            const shadeByte = (unsigned >> 8) & 0xFF;
            const tintByte = unsigned & 0xFF;

            // Check if this is actually a theme color (0xF0-0xFF range)
            if ((themeColorByte & 0xF0) === 0xF0) {
                const themeIndex = themeColorByte & 0x0F;

                // Calculate tint/shade adjustment
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

                // Find base color for this theme index
                const baseColor = getBaseThemeColor(themeIndex);
                if (baseColor) {
                    return applyTintShade(baseColor, tintShade);
                }
            }
        }

        // Fallback to hard-coded mapping for safety
        const themeColorMap = {
            '-603914241': '#000000', // Text 1
            '-587137025': '#FFFFFF', // Background 1
            '-570359809': '#44546A', // Text 2
            '-553582593': '#E7E6E6', // Background 2
            '-738131969': '#4472C4', // Accent 1
            '-721354753': '#E15759', // Accent 2
            '-704577537': '#70AD47', // Accent 3
            '-687800321': '#FFC000', // Accent 4
            '-671023105': '#5B9BD5', // Accent 5
            '-654245889': '#A5A5A5'  // Accent 6
        };

        return themeColorMap[decimal.toString()] || '#000000';
    }

    // Get base theme color by index (for dynamic resolution)
    function getBaseThemeColor(themeIndex) {
        const baseColor = themeColors.base.find(color => color.themeIndex === themeIndex);
        return baseColor ? baseColor.hex : null;
    }

    // Decode Word theme color decimal to components (for debugging/analysis)
    function decodeThemeColor(decimal) {
        if (decimal >= 0) return null; // Not a theme color

        // Convert to unsigned 32-bit for bit manipulation
        const unsigned = decimal >>> 0;

        // Extract components
        const themeColorByte = (unsigned >> 24) & 0xFF;
        const shadeByte = (unsigned >> 8) & 0xFF;
        const tintByte = unsigned & 0xFF;

        // Check if this is a theme color
        if ((themeColorByte & 0xF0) !== 0xF0) return null;

        const themeIndex = themeColorByte & 0x0F;

        // Calculate tint/shade
        let tintShade = 0;
        const unchanged = 0xFF;

        if (shadeByte !== unchanged) {
            tintShade = Math.round((-1 + shadeByte / 255) * 100) / 100;
        }

        if (tintByte !== unchanged) {
            tintShade = Math.round((1 - tintByte / 255) * 100) / 100;
        }

        return {
            isThemeColor: true,
            themeIndex: themeIndex,
            tintShade: tintShade,
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

    // Apply tint/shade to a color
    function applyTintShade(hexColor, tintShade) {
        const cleanedHex = hexColor.replace('#', '');
        const r = parseInt(cleanedHex.substring(0, 2), 16);
        const g = parseInt(cleanedHex.substring(2, 4), 16);
        const b = parseInt(cleanedHex.substring(4, 6), 16);

        const [h, s, l] = rgbToHsl(r, g, b);

        // Apply tint (lighter) or shade (darker)
        let newL;
        if (tintShade > 0) {
            // Tint - make lighter
            newL = l + (1 - l) * tintShade;
        } else {
            // Shade - make darker
            newL = l * (1 + tintShade);
        }

        newL = Math.max(0, Math.min(1, newL));

        const [newR, newG, newB] = hslToRgb(h, s, newL);

        return `#${newR.toString(16).padStart(2, '0')}${newG.toString(16).padStart(2, '0')}${newB.toString(16).padStart(2, '0')}`.toUpperCase();
    }

    // Generate theme color with tint/shade decimal value for Word
    function generateThemeColorDecimal(themeIndex, tintShade) {
        // Word 2007+ theme color format: 0xF[I][00][SS][TT] where:
        // F = Theme color indicator (0xF0)
        // I = Theme color index (0-15, lower 4 bits)
        // 00 = Reserved byte (always 0x00)
        // SS = Shade value (255 = unchanged, lower = darker)
        // TT = Tint value (255 = unchanged, lower = lighter)

        let shadeValue = 0xFF; // 255 = unchanged
        let tintValue = 0xFF;  // 255 = unchanged

        if (tintShade < 0) {
            // Shade (darker) - calculate darkness byte
            // Formula from Word Articles: Round(-1 + DarknessByte / 255, 2)
            // Solving for DarknessByte: (tintShade + 1) * 255
            shadeValue = Math.round((tintShade + 1) * 255);
            shadeValue = Math.max(0, Math.min(255, shadeValue));
        } else if (tintShade > 0) {
            // Tint (lighter) - calculate lightness byte
            // Formula from Word Articles: Round(1 - LightnessByte / 255, 2)
            // Solving for LightnessByte: (1 - tintShade) * 255
            tintValue = Math.round((1 - tintShade) * 255);
            tintValue = Math.max(0, Math.min(255, tintValue));
        }

        // Construct the theme color decimal value
        // Format: 0xF[themeIndex][00][shade][tint]
        const themeColorByte = 0xF0 | (themeIndex & 0x0F);

        // Create the 32-bit value
        const result = (themeColorByte << 24) | (0x00 << 16) | (shadeValue << 8) | tintValue;

        // Convert to signed 32-bit integer (as Word expects)
        return result > 0x7FFFFFFF ? result - 0x100000000 : result;
    }

    // Initialize decimal values for theme and standard colors
    function initializeColorDecimals() {
        // Initialize theme colors with decimal values
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
        // Populate theme colors - first row (base colors)
        const themeColorsGrid = document.getElementById('theme-colors-grid');
        themeColorsGrid.innerHTML = ''; // Clear existing

        // Add base theme colors (first row)
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

        // Add theme color variations (tint/shade rows)
        themeColors.variations.forEach(variation => {
            themeColors.base.forEach(baseColor => {
                const tintShade = variation.tint ? variation.tint : -variation.shade;
                const adjustedHex = applyTintShade(baseColor.hex, tintShade);
                const adjustedDecimal = generateThemeColorDecimal(baseColor.themeIndex, tintShade);

                const swatch = createColorSwatch(adjustedHex, {
                    type: 'theme',
                    name: `${baseColor.name} ${variation.label}`,
                    themeIndex: baseColor.themeIndex,
                    decimal: adjustedDecimal,
                    tintShade: tintShade
                });
                themeColorsGrid.appendChild(swatch);
            });
        });

        // Populate standard colors
        const standardColorsGrid = document.getElementById('standard-colors-grid');
        standardColorsGrid.innerHTML = ''; // Clear existing

        standardColors.forEach(color => {
            const swatch = createColorSwatch(color.hex, {
                type: 'standard',
                name: color.name,
                decimal: color.decimal
            });
            standardColorsGrid.appendChild(swatch);
        });

        // Auto and No Color buttons
        document.querySelector('.auto-color').addEventListener('click', () => {
            selectColor('#000000', {
                type: 'auto',
                name: 'Automatic',
                decimal: -16777216 // Word's automatic color value
            });
        });

        document.querySelector('.no-color').addEventListener('click', () => {
            selectColor(null, {
                type: 'none',
                name: 'No Color',
                decimal: null
            });
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
                // Here you would send the colorData.decimal value to C# VSTO
                notifyColorChange('find', colorData);

            } else if (state.currentColorTarget.id === 'replace-color-toggle') {
                // Here you would send the colorData.decimal value to C# VSTO
                notifyColorChange('replace', colorData);
            }
        }

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
