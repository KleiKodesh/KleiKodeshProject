/**
 * Hebrew Fonts Service
 * 
 * Provides Hebrew font definitions and utilities.
 * Prioritizes fonts with niqqud and ta'amim support for Biblical Hebrew texts.
 */

// Hebrew fonts: Culmus/Kulmus Project (prioritized), System fonts, Google Fonts
// Prioritizing fonts with niqqud and ta'amim support for Biblical Hebrew texts
export const hebrewFonts = [
    // === CULMUS/KULMUS PROJECT - PREMIUM (NIQQUD + TA'AMIM) ===
    // Fonts specifically designed for Biblical Hebrew with cantillation marks

    'Taamey Ashkenaz',    // Ashkenazi cantillation tradition - BEST for Biblical Hebrew
    'Taamey David CLM',   // David font with cantillation marks
    'Taamey Frank CLM',   // Frank Ruehl with cantillation marks
    'Keter YG',           // Modernized Aleppo Codex style (4 weights)
    'Keter Aram Tsova',   // Based on Aleppo Codex manuscript
    'Shofar',             // Hebrew font with cantillation support

    // === CULMUS/KULMUS PROJECT - EXCELLENT NIQQUD SUPPORT ===

    'Frank Ruehl CLM',    // Traditional serif Hebrew (4 weights) - excellent niqqud
    'David CLM',          // Serif Hebrew based on Charter (3 weights) - good niqqud
    'Miriam CLM',         // Hebrew font (2 weights) - good niqqud
    'Miriam Mono CLM',    // Monospace Hebrew (4 weights) - good niqqud
    'Simple CLM',         // Basic Hebrew font with romanization and niqqud
    'Nachlieli CLM',      // Sans-serif Hebrew (4 weights) - good niqqud
    'Aharoni CLM',        // Sans-serif Hebrew (4 weights)

    // === CULMUS/KULMUS PROJECT - GOOD HEBREW SUPPORT ===

    'Drugulin CLM',       // Bold serif based on Nimbus Roman
    'Ellinia CLM',        // Hebrew font family (4 weights)
    'Hadasim CLM',        // Hebrew font
    'Yehuda CLM',         // Hebrew font based on Tekton (2 weights)
    'Stam Ashkenaz CLM',  // Traditional Torah scroll Ashkenazi style
    'Stam Sefarad CLM',   // Traditional Torah scroll Sephardic style
    'Farissol CLM',       // Italic Hebrew (extracted from Drugulin v0.140)
    'Fixed Miriam Transparent', // Fixed-width transparent Hebrew
    'Gan CLM',            // Hebrew font
    'Journal CLM',        // Hebrew font
    'Ktav Yad CLM',       // Handwriting-style Hebrew
    'Ozrad CLM',          // Hebrew font
    'Comix No2 CLM',      // Comic-style Hebrew

    // === YOUR SYSTEM - PREMIUM HEBREW FONTS ===
    // Traditional Hebrew fonts with excellent diacritical support

    'David',              // Traditional Hebrew serif - excellent niqqud support
    'Miriam',             // Classic Hebrew font - full vowel support
    'FrankRuehl',         // Traditional Hebrew serif - good niqqud support

    // Professional Guttman fonts (your system has excellent coverage)
    'Guttman Stam',       // Traditional Torah scroll style
    'Guttman Stam1',      // Alternative Torah scroll style
    'Guttman Frank',      // Frank Ruehl style with diacritics
    'Guttman Vilna',      // Traditional Vilna style
    'Guttman Rashi',      // Rashi script style

    // === YOUR SYSTEM - EXCELLENT NIQQUD SUPPORT ===

    // Windows Hebrew fonts with confirmed vowel support
    'Gisha',              // Modern Hebrew sans-serif with niqqud (Vista+)
    'Arial',              // Full Hebrew support including vowels
    'Times New Roman',    // Hebrew support with vowels
    'Miriam Fixed',       // Fixed-width Hebrew with vowels
    'Aharoni',            // Hebrew display font with vowels

    // Professional Guttman collection with good Hebrew support
    'Guttman Aharoni',    // Professional Aharoni variant
    'Guttman Yad',        // Handwriting style Hebrew
    'Guttman Yad-Brush',  // Brush handwriting Hebrew
    'Guttman Yad-Light',  // Light handwriting Hebrew
    'Guttman Haim',       // Hebrew font family
    'Guttman Haim-Condensed', // Condensed Hebrew
    'Guttman Hatzvi',     // Hebrew display font
    'Guttman Kav',        // Hebrew font with good spacing
    'Guttman Kav-Light',  // Light variant
    'Guttman Mantova',    // Decorative Hebrew
    'Guttman Mantova-Decor', // Decorative variant
    'Guttman Miryam',     // Hebrew font variant
    'Guttman Drogolin',   // Hebrew serif style
    'Guttman Frnew',      // Modern Hebrew variant
    'Guttman Logo1',      // Display Hebrew font
    'Guttman Myamfix',    // Fixed Hebrew font

    // === GOOGLE FONTS - HEBREW WITH NIQQUD SUPPORT ===

    'Noto Sans Hebrew',   // Google's comprehensive Hebrew sans with niqqud
    'Noto Serif Hebrew',  // Google's comprehensive Hebrew serif with niqqud
    'Frank Ruhl Libre',   // Open source Hebrew serif with niqqud
    'IBM Plex Sans Hebrew', // IBM's Hebrew font with niqqud support
    'Miriam Libre',       // Open source Hebrew font

    // === GOOGLE FONTS - MODERN HEBREW FONTS ===

    'Alef',               // Hebrew sans-serif font family
    'Assistant',          // Modern Hebrew sans-serif
    'Heebo',              // Contemporary Hebrew sans-serif
    'Rubik',              // Modern Hebrew sans-serif
    'Secular One',        // Hebrew display font
    'Suez One',           // Hebrew serif display font
    'Karantina',          // Display Hebrew font
    'Amatic SC',          // Handwritten Hebrew style
    'Varela Round',       // Rounded Hebrew sans-serif

    // === YOUR SYSTEM - GOOD HEBREW SUPPORT ===

    // Modern Windows fonts with Hebrew support
    'Calibri',            // Modern sans-serif with Hebrew
    'Calibri Light',      // Light variant
    'Segoe UI',           // Modern UI font with Hebrew
    'Segoe UI Light',     // Light UI variant
    'Segoe UI Semibold',  // Semibold UI variant
    'Segoe UI Semilight', // Semilight UI variant
    'Tahoma',             // Good Hebrew rendering

    // Additional Guttman fonts
    'Guttman-Aharoni',    // Alternative Aharoni
    'Guttman-Aram',       // Aramaic style Hebrew
    'Guttman-CourMir',    // Courier Miriam style

    // === GOOGLE FONTS - BASIC HEBREW SUPPORT ===

    'Arimo',              // Liberation Sans with Hebrew
    'Cousine',            // Liberation Mono with Hebrew
    'Tinos',              // Liberation Serif with Hebrew

    // === YOUR SYSTEM - BASIC HEBREW SUPPORT ===

    // Arial variants with Hebrew support
    'Arial Black',        // Bold Arial with Hebrew
    'Arial Narrow',       // Narrow Arial with Hebrew
    'Arial Nova',         // Modern Arial with Hebrew
    'Arial Nova Light',   // Light modern Arial
    'Arial Nova Cond',    // Condensed modern Arial
    'Arial Nova Cond Light', // Light condensed Arial
    'Arial Rounded MT Bold', // Rounded Arial with Hebrew

    // Franklin Gothic variants (basic Hebrew support)
    'Franklin Gothic Book',
    'Franklin Gothic Medium',
    'Franklin Gothic Demi',
    'Franklin Gothic Heavy',
    'Franklin Gothic Medium Cond',
    'Franklin Gothic Demi Cond',

    // Segoe UI Variable fonts (modern Hebrew support)
    'Segoe UI Variable Text',
    'Segoe UI Variable Text Light',
    'Segoe UI Variable Text Semibold',
    'Segoe UI Variable Display',
    'Segoe UI Variable Display Light',
    'Segoe UI Variable Small',
    'Segoe UI Variable Small Light',

    // Additional Segoe fonts
    'Segoe Print',        // Handwriting style with Hebrew
    'Segoe Script'        // Script style with Hebrew
]

/**
 * Get all available Hebrew fonts
 */
export function getAllFonts(): string[] {
    return [...hebrewFonts]
}

/**
 * Get fonts with premium niqqud and ta'amim support
 */
export function getPremiumFonts(): string[] {
    return hebrewFonts.slice(0, 6) // First 6 are premium fonts
}

/**
 * Get fonts with excellent niqqud support
 */
export function getExcellentNiqqudFonts(): string[] {
    return hebrewFonts.slice(6, 13) // Next 7 are excellent niqqud fonts
}

/**
 * Get system fonts with good Hebrew support
 */
export function getSystemFonts(): string[] {
    return hebrewFonts.filter(font =>
        font.includes('David') ||
        font.includes('Miriam') ||
        font.includes('Guttman') ||
        font.includes('Arial') ||
        font.includes('Segoe') ||
        font.includes('Calibri') ||
        font.includes('Tahoma')
    )
}

/**
 * Get Google Fonts with Hebrew support
 */
export function getGoogleFonts(): string[] {
    return hebrewFonts.filter(font =>
        font.includes('Noto') ||
        font.includes('Frank Ruhl Libre') ||
        font.includes('IBM Plex') ||
        font.includes('Miriam Libre') ||
        font.includes('Alef') ||
        font.includes('Assistant') ||
        font.includes('Heebo') ||
        font.includes('Rubik')
    )
}

/**
 * Check if a font is available in the system
 */
export async function isFontAvailable(fontName: string): Promise<boolean> {
    if (!document.fonts) {
        return false
    }

    try {
        return await document.fonts.check(`12px "${fontName}"`)
    } catch {
        return false
    }
}

/**
 * Get the best available font for Hebrew text
 */
export async function getBestAvailableFont(): Promise<string> {
    const premiumFonts = getPremiumFonts()

    for (const font of premiumFonts) {
        if (await isFontAvailable(font)) {
            return font
        }
    }

    // Fallback to system fonts
    const systemFonts = ['David', 'Miriam', 'Arial', 'Segoe UI']
    for (const font of systemFonts) {
        if (await isFontAvailable(font)) {
            return font
        }
    }

    return 'Arial' // Ultimate fallback
}

/**
 * Create CSS font-family string with fallbacks
 */
export function createFontFamilyString(primaryFont: string): string {
    const fallbacks = ['David', 'Miriam', 'Arial', 'sans-serif']
    const fonts = [primaryFont, ...fallbacks.filter(f => f !== primaryFont)]
    return fonts.map(font => `"${font}"`).join(', ')
}