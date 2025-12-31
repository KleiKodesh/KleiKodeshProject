// Main entry point for Vite build
import '../css/theme.css';
import '../css/buttons.css';
import '../css/regex-find.css';
import '../css/color-picker.css';
import '../css/input-wrapper.css';

// Import all JavaScript modules
import '../js/webview-bridge.js';
import '../js/font-manager.js';
import '../js/custom-combobox.js';
import '../js/regex-palette.js';
import '../js/toggle-buttons.js';
import '../js/color-picker.js';
import '../js/main.js';

// Import regex tips data
import { regexTips } from '../js/regexTips.js';

// Make regex tips available globally for the regex palette
window.regexTipsData = regexTips;

// Initialize everything when DOM is loaded
document.addEventListener('DOMContentLoaded', async () => {
    console.log('Initializing Regex Find Tool...');
    
    // Initialize regex palette
    if (typeof RegexPalette !== 'undefined') {
        const regexPalette = new RegexPalette();
        await regexPalette.initialize();
        window.regexPalette = regexPalette;
    }
    
    // Initialize color picker
    if (typeof ColorPicker !== 'undefined') {
        const colorPicker = new ColorPicker();
        colorPicker.initialize();
        window.colorPicker = colorPicker;
    }
    
    // Initialize webview bridge
    if (typeof WebViewBridge !== 'undefined') {
        const webViewBridge = new WebViewBridge();
        webViewBridge.setupMessageListener();
        window.webViewBridge = webViewBridge;
    }
    
    console.log('Regex Find Tool initialized successfully');
});