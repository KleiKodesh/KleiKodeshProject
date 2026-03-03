/**
 * Hebrew Font Detection Script
 * Based on the method from tchumim.com - tests actual Hebrew character rendering
 * This script enumerates system fonts and tests Hebrew Unicode support
 */

const fs = require('fs');
const path = require('path');

// Hebrew Unicode ranges to test
const HEBREW_TEST_CHARS = {
    basic: '\u05D0\u05D1\u05D2\u05D3\u05D4', // אבגדה - Basic Hebrew letters
    niqqud: '\u05B0\u05B1\u05B2\u05B3\u05B4', // Vowel marks (niqqud)
    cantillation: '\u0591\u0592\u0593\u0594\u0595', // Cantillation marks (ta'amim)
    finalForms: '\u05DA\u05DD\u05DF\u05E3\u05E5', // Final forms כםןףץ
    yiddish: '\u05F0\u05F1\u05F2\u05F3\u05F4' // Yiddish ligatures and punctuation
};

// Create HTML test file to check font rendering
function createFontTestHTML() {
    const testChars = Object.values(HEBREW_TEST_CHARS).join('');

    return `
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Hebrew Font Detection</title>
    <style>
        .font-test {
            font-size: 16px;
            margin: 2px 0;
            white-space: nowrap;
        }
        .test-chars {
            display: inline-block;
            width: 200px;
        }
    </style>
</head>
<body>
    <h1>Hebrew Font Detection Test</h1>
    <div id="results"></div>
    
    <script>
        // List of fonts to test (will be populated by Node.js)
        const fontsToTest = [FONTS_PLACEHOLDER];
        
        const testResults = {};
        
        // Test each font
        fontsToTest.forEach(fontName => {
            const testDiv = document.createElement('div');
            testDiv.className = 'font-test';
            testDiv.style.fontFamily = fontName + ', monospace';
            
            const results = {};
            
            // Test different Hebrew character sets
            Object.entries({
                basic: '${HEBREW_TEST_CHARS.basic}',
                niqqud: '${HEBREW_TEST_CHARS.niqqud}',
                cantillation: '${HEBREW_TEST_CHARS.cantillation}',
                finalForms: '${HEBREW_TEST_CHARS.finalForms}',
                yiddish: '${HEBREW_TEST_CHARS.yiddish}'
            }).forEach(([type, chars]) => {
                const span = document.createElement('span');
                span.textContent = chars;
                span.className = 'test-chars';
                testDiv.appendChild(span);
                
                // Check if characters render properly (not as boxes/fallback)
                const canvas = document.createElement('canvas');
                const ctx = canvas.getContext('2d');
                ctx.font = '16px "' + fontName + '"';
                
                let hasSupport = true;
                for (let char of chars) {
                    const metrics = ctx.measureText(char);
                    if (metrics.width === 0) {
                        hasSupport = false;
                        break;
                    }
                }
                
                results[type] = hasSupport;
            });
            
            testResults[fontName] = results;
            
            const label = document.createElement('span');
            label.textContent = fontName + ': ';
            label.style.fontWeight = 'bold';
            label.style.width = '200px';
            label.style.display = 'inline-block';
            
            testDiv.insertBefore(label, testDiv.firstChild);
            document.getElementById('results').appendChild(testDiv);
        });
        
        // Output results to console for Node.js to capture
        console.log('HEBREW_FONT_RESULTS:', JSON.stringify(testResults, null, 2));
    </script>
</body>
</html>`;
}

// PowerShell script to get system fonts
function getSystemFontsScript() {
    return `
Add-Type -AssemblyName System.Drawing
$fonts = [System.Drawing.FontFamily]::Families
$fontNames = @()
foreach ($font in $fonts) {
    $fontNames += $font.Name
}
$fontNames | ConvertTo-Json
`;
}

// Main detection function
async function detectHebrewFonts() {
    console.log('🔍 Detecting Hebrew fonts on your system...\n');

    try {
        // Step 1: Get list of system fonts using PowerShell
        console.log('📋 Getting system fonts...');
        const { execSync } = require('child_process');

        const fontsJson = execSync('powershell -Command "' + getSystemFontsScript() + '"', {
            encoding: 'utf8'
        });

        const systemFonts = JSON.parse(fontsJson);
        console.log(`Found ${systemFonts.length} system fonts`);

        // Step 2: Filter to likely Hebrew fonts first (performance optimization)
        const likelyHebrewFonts = systemFonts.filter(font => {
            const lowerFont = font.toLowerCase();
            return lowerFont.includes('hebrew') ||
                lowerFont.includes('david') ||
                lowerFont.includes('miriam') ||
                lowerFont.includes('aharoni') ||
                lowerFont.includes('gisha') ||
                lowerFont.includes('frank') ||
                lowerFont.includes('culmus') ||
                lowerFont.includes('clm') ||
                lowerFont.includes('guttman') ||
                lowerFont.includes('keter') ||
                lowerFont.includes('taamey') ||
                lowerFont.includes('noto') ||
                lowerFont.includes('arial') ||
                lowerFont.includes('times') ||
                lowerFont.includes('calibri') ||
                lowerFont.includes('segoe') ||
                lowerFont.includes('tahoma');
        });

        console.log(`Filtered to ${likelyHebrewFonts.length} likely Hebrew fonts for testing`);

        // Step 3: Test Hebrew support using browser rendering
        const puppeteer = require('puppeteer-core');
        let browser;

        try {
            // Try to find Chrome/Edge executable
            const possiblePaths = [
                'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
                'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
                'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
                'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'
            ];

            let executablePath = null;
            for (const path of possiblePaths) {
                if (fs.existsSync(path)) {
                    executablePath = path;
                    break;
                }
            }

            if (!executablePath) {
                throw new Error('Chrome or Edge not found');
            }

            browser = await puppeteer.launch({
                executablePath,
                headless: true
            });

            const page = await browser.newPage();

            // Create test HTML with font list
            const testHTML = createFontTestHTML().replace(
                '[FONTS_PLACEHOLDER]',
                JSON.stringify(likelyHebrewFonts)
            );

            await page.setContent(testHTML);

            // Wait for test to complete and get results
            const results = await page.evaluate(() => {
                return new Promise((resolve) => {
                    setTimeout(() => {
                        // Results should be in console by now
                        resolve(window.testResults || {});
                    }, 2000);
                });
            });

            await browser.close();

            // Step 4: Analyze results and categorize fonts
            const hebrewFonts = {
                premium: [], // Niqqud + Ta'amim
                excellent: [], // Niqqud support
                good: [], // Basic + Final forms
                basic: [] // Basic Hebrew only
            };

            Object.entries(results).forEach(([fontName, support]) => {
                if (support.basic) {
                    if (support.cantillation && support.niqqud) {
                        hebrewFonts.premium.push(fontName);
                    } else if (support.niqqud) {
                        hebrewFonts.excellent.push(fontName);
                    } else if (support.finalForms) {
                        hebrewFonts.good.push(fontName);
                    } else {
                        hebrewFonts.basic.push(fontName);
                    }
                }
            });

            // Step 5: Output results
            console.log('\n🎯 Hebrew Font Detection Results:\n');

            console.log('🏆 PREMIUM (Niqqud + Ta\'amim):');
            hebrewFonts.premium.forEach(font => console.log(`  ✓ ${font}`));

            console.log('\n🥇 EXCELLENT (Niqqud Support):');
            hebrewFonts.excellent.forEach(font => console.log(`  ✓ ${font}`));

            console.log('\n🥈 GOOD (Basic + Final Forms):');
            hebrewFonts.good.forEach(font => console.log(`  ✓ ${font}`));

            console.log('\n🥉 BASIC (Hebrew Letters Only):');
            hebrewFonts.basic.forEach(font => console.log(`  ✓ ${font}`));

            // Step 6: Generate updated hebrewFonts.ts
            const allDetectedFonts = [
                ...hebrewFonts.premium,
                ...hebrewFonts.excellent,
                ...hebrewFonts.good,
                ...hebrewFonts.basic
            ];

            console.log(`\n📝 Total Hebrew fonts detected: ${allDetectedFonts.length}`);
            console.log('\n💾 Generating updated hebrewFonts.ts...');

            const updatedFontsFile = generateUpdatedFontsFile(hebrewFonts);
            fs.writeFileSync(
                path.join(__dirname, '../src/data/hebrewFonts.detected.ts'),
                updatedFontsFile
            );

            console.log('✅ Saved detected fonts to: src/data/hebrewFonts.detected.ts');
            console.log('\nYou can review and replace the original hebrewFonts.ts if satisfied with results.');

        } catch (browserError) {
            console.log('⚠️  Browser testing failed, falling back to basic detection...');
            console.log('Error:', browserError.message);

            // Fallback: just return the filtered list
            const basicResults = {
                basic: likelyHebrewFonts,
                excellent: [],
                good: [],
                premium: []
            };

            const fallbackFile = generateUpdatedFontsFile(basicResults);
            fs.writeFileSync(
                path.join(__dirname, '../src/data/hebrewFonts.detected.ts'),
                fallbackFile
            );

            console.log('✅ Saved basic detected fonts to: src/data/hebrewFonts.detected.ts');
        }

    } catch (error) {
        console.error('❌ Error detecting fonts:', error.message);
        process.exit(1);
    }
}

function generateUpdatedFontsFile(hebrewFonts) {
    return `// Auto-detected Hebrew fonts from your system
// Generated on ${new Date().toISOString()}
export const hebrewFonts = [
    // === PREMIUM HEBREW FONTS (NIQQUD + TA'AMIM SUPPORT) ===
${hebrewFonts.premium.map(font => `    '${font}',`).join('\n')}

    // === EXCELLENT NIQQUD SUPPORT (VOWEL MARKS) ===
${hebrewFonts.excellent.map(font => `    '${font}',`).join('\n')}

    // === GOOD HEBREW SUPPORT (BASIC + FINAL FORMS) ===
${hebrewFonts.good.map(font => `    '${font}',`).join('\n')}

    // === BASIC HEBREW SUPPORT ===
${hebrewFonts.basic.map(font => `    '${font}',`).join('\n')}
];

// Detection summary:
// Premium fonts: ${hebrewFonts.premium.length}
// Excellent fonts: ${hebrewFonts.excellent.length}  
// Good fonts: ${hebrewFonts.good.length}
// Basic fonts: ${hebrewFonts.basic.length}
// Total: ${hebrewFonts.premium.length + hebrewFonts.excellent.length + hebrewFonts.good.length + hebrewFonts.basic.length}
`;
}

// Run the detection
if (require.main === module) {
    detectHebrewFonts().catch(console.error);
}

module.exports = { detectHebrewFonts };