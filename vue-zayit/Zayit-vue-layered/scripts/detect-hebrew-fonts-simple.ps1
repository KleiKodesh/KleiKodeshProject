# Hebrew Font Detection Script (PowerShell)
# Based on tchumim.com method - tests actual Hebrew Unicode support

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# Hebrew Unicode ranges to test
$HebrewRanges = @{
    BasicHebrew = @(0x05D0..0x05EA)      # א-ת Basic Hebrew letters
    HebrewPoints = @(0x05B0..0x05BD)     # Niqqud (vowel points)
    Cantillation = @(0x0591..0x05AF)     # Ta'amim (cantillation marks)
    FinalForms = @(0x05DA, 0x05DD, 0x05DF, 0x05E3, 0x05E5)  # ךםןףץ
    HebrewPunctuation = @(0x05BE, 0x05C0, 0x05C3, 0x05C6)   # Hebrew punctuation
}

function Test-FontHebrewSupport {
    param(
        [string]$FontName,
        [hashtable]$UnicodeRanges
    )
    
    $results = @{}
    
    try {
        # Create a font object
        $font = New-Object System.Drawing.Font($FontName, 12)
        
        # Create a bitmap and graphics object for testing
        $bitmap = New-Object System.Drawing.Bitmap(100, 100)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        
        foreach ($rangeName in $UnicodeRanges.Keys) {
            $supportCount = 0
            $totalChars = $UnicodeRanges[$rangeName].Count
            
            foreach ($codePoint in $UnicodeRanges[$rangeName]) {
                $char = [char]$codePoint
                
                # Measure the character
                $size = $graphics.MeasureString($char, $font)
                
                # Check if the character has a reasonable width (not a missing glyph)
                if ($size.Width -gt 2) {
                    $supportCount++
                }
            }
            
            # Calculate support percentage
            $supportPercentage = ($supportCount / $totalChars) * 100
            $results[$rangeName] = @{
                SupportCount = $supportCount
                TotalChars = $totalChars
                Percentage = [math]::Round($supportPercentage, 1)
                HasSupport = $supportPercentage -gt 50
            }
        }
        
        $graphics.Dispose()
        $bitmap.Dispose()
        $font.Dispose()
        
    } catch {
        Write-Warning "Error testing font '$FontName': $($_.Exception.Message)"
        return $null
    }
    
    return $results
}

function Get-HebrewFontCategory {
    param($TestResults)
    
    if (-not $TestResults -or -not $TestResults.BasicHebrew.HasSupport) {
        return "None"
    }
    
    $hasBasic = $TestResults.BasicHebrew.HasSupport
    $hasNiqqud = $TestResults.HebrewPoints.HasSupport
    $hasCantillation = $TestResults.Cantillation.HasSupport
    $hasFinalForms = $TestResults.FinalForms.HasSupport
    
    if ($hasBasic -and $hasNiqqud -and $hasCantillation) {
        return "Premium"
    } elseif ($hasBasic -and $hasNiqqud) {
        return "Excellent"
    } elseif ($hasBasic -and $hasFinalForms) {
        return "Good"
    } elseif ($hasBasic) {
        return "Basic"
    } else {
        return "None"
    }
}

# Main execution
Write-Host "Detecting Hebrew fonts on your system..." -ForegroundColor Cyan
Write-Host ""

# Get all system fonts
$systemFonts = [System.Drawing.FontFamily]::Families
Write-Host "Found $($systemFonts.Count) system fonts" -ForegroundColor Green

# Filter to likely Hebrew fonts for performance
$likelyHebrewFonts = $systemFonts | Where-Object { 
    $name = $_.Name.ToLower()
    $name -match "hebrew|david|miriam|aharoni|gisha|frank|culmus|clm|guttman|keter|taamey|noto|arial|times|calibri|segoe|tahoma|courier|consolas"
}

Write-Host "Testing $($likelyHebrewFonts.Count) likely Hebrew fonts..." -ForegroundColor Yellow
Write-Host ""

# Test each font
$hebrewFonts = @{
    Premium = @()
    Excellent = @()
    Good = @()
    Basic = @()
}

$testResults = @{}

foreach ($font in $likelyHebrewFonts) {
    Write-Host "Testing: $($font.Name)..." -NoNewline
    
    $results = Test-FontHebrewSupport -FontName $font.Name -UnicodeRanges $HebrewRanges
    
    if ($results) {
        $category = Get-HebrewFontCategory -TestResults $results
        
        if ($category -ne "None") {
            $hebrewFonts[$category] += $font.Name
            $testResults[$font.Name] = $results
            
            Write-Host " ✓ $category" -ForegroundColor Green
        } else {
            Write-Host " ✗ No Hebrew support" -ForegroundColor Red
        }
    } else {
        Write-Host " ⚠ Error testing" -ForegroundColor Yellow
    }
}

# Display results
Write-Host ""
Write-Host "Hebrew Font Detection Results:" -ForegroundColor Cyan
Write-Host ""

Write-Host "PREMIUM (Niqqud + Ta'amim): $($hebrewFonts.Premium.Count)" -ForegroundColor Magenta
foreach ($font in $hebrewFonts.Premium) {
    $result = $testResults[$font]
    Write-Host "  ✓ $font (N:$($result.HebrewPoints.Percentage)% T:$($result.Cantillation.Percentage)%)" -ForegroundColor White
}

Write-Host ""
Write-Host "EXCELLENT (Niqqud Support): $($hebrewFonts.Excellent.Count)" -ForegroundColor Green
foreach ($font in $hebrewFonts.Excellent) {
    $result = $testResults[$font]
    Write-Host "  ✓ $font (N:$($result.HebrewPoints.Percentage)%)" -ForegroundColor White
}

Write-Host ""
Write-Host "GOOD (Basic + Final Forms): $($hebrewFonts.Good.Count)" -ForegroundColor Blue
foreach ($font in $hebrewFonts.Good) {
    Write-Host "  ✓ $font" -ForegroundColor White
}

Write-Host ""
Write-Host "BASIC (Hebrew Letters Only): $($hebrewFonts.Basic.Count)" -ForegroundColor Gray
foreach ($font in $hebrewFonts.Basic) {
    Write-Host "  ✓ $font" -ForegroundColor White
}

# Generate TypeScript file
$totalFonts = $hebrewFonts.Premium.Count + $hebrewFonts.Excellent.Count + $hebrewFonts.Good.Count + $hebrewFonts.Basic.Count

Write-Host ""
Write-Host "Total Hebrew fonts detected: $totalFonts" -ForegroundColor Cyan

# Create TypeScript content
$tsContent = @"
// Auto-detected Hebrew fonts from your system
// Generated on $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
export const hebrewFonts = [
    // === PREMIUM HEBREW FONTS (NIQQUD + TA'AMIM SUPPORT) ===
$($hebrewFonts.Premium | ForEach-Object { "    '$_'," } | Out-String)
    // === EXCELLENT NIQQUD SUPPORT (VOWEL MARKS) ===
$($hebrewFonts.Excellent | ForEach-Object { "    '$_'," } | Out-String)
    // === GOOD HEBREW SUPPORT (BASIC + FINAL FORMS) ===
$($hebrewFonts.Good | ForEach-Object { "    '$_'," } | Out-String)
    // === BASIC HEBREW SUPPORT ===
$($hebrewFonts.Basic | ForEach-Object { "    '$_'," } | Out-String)
];

// Detection summary:
// Premium fonts: $($hebrewFonts.Premium.Count)
// Excellent fonts: $($hebrewFonts.Excellent.Count)
// Good fonts: $($hebrewFonts.Good.Count)
// Basic fonts: $($hebrewFonts.Basic.Count)
// Total: $totalFonts
"@

# Save to file
$outputPath = Join-Path $PSScriptRoot "../src/data/hebrewFonts.detected.ts"
$tsContent | Out-File -FilePath $outputPath -Encoding UTF8

Write-Host "Saved detected fonts to: src/data/hebrewFonts.detected.ts" -ForegroundColor Green
Write-Host ""
Write-Host "Detection complete! Review the results and replace hebrewFonts.ts if satisfied." -ForegroundColor Green