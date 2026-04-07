/**
 * High-DPI PDF.js rendering enhancement
 * Based on: https://stackoverflow.com/questions/49426385/how-to-fix-pdf-documents-from-being-rendered-in-really-low-resolution-blurry
 */

// Force higher pixel ratio for sharper rendering
const originalDevicePixelRatio = window.devicePixelRatio || 1;
const enhancedPixelRatio = Math.max(originalDevicePixelRatio, 2); // Minimum 2x for sharpness

// Override the devicePixelRatio property
Object.defineProperty(window, 'devicePixelRatio', {
    get: function () {
        return enhancedPixelRatio;
    },
    configurable: true
});

console.log(`[PDF.js Enhancement] Original devicePixelRatio: ${originalDevicePixelRatio}, Enhanced: ${enhancedPixelRatio}`);