# Test script for website update logic
Write-Host "Testing website update logic..." -ForegroundColor Green

# Simulate release data
$version = "2.9.0"
$repoUrl = "https://github.com/KleiKodesh/KleiKodeshProject"
$setupFileName = "KleiKodeshSetup-2.9.0.exe"
$downloadUrl = "$repoUrl/releases/download/$version/$setupFileName"

Write-Host "Simulated version: $version" -ForegroundColor Cyan
Write-Host "Download URL: $downloadUrl" -ForegroundColor Cyan

# Get paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$websitePath = Join-Path $projectRoot "kleikodesh.github.io"
$indexPath = Join-Path $websitePath "index.html"

Write-Host ""
Write-Host "Website path: $websitePath" -ForegroundColor Gray
Write-Host "Index path: $indexPath" -ForegroundColor Gray

if (-not (Test-Path $indexPath)) {
    Write-Host "ERROR: index.html not found at $indexPath" -ForegroundColor Red
    exit 1
}

# Read the HTML file
$htmlContent = Get-Content $indexPath -Raw -Encoding UTF8

# Find current download link
$currentLinkMatch = [regex]::Match($htmlContent, '<a href="([^"]+)"\s+class="inline-flex items-center gap-2[^>]+>\s+<svg[^>]+>[\s\S]*?</svg>\s+הורד את ההתקנה כעת')

if ($currentLinkMatch.Success) {
    Write-Host ""
    Write-Host "Current download link found:" -ForegroundColor Green
    Write-Host $currentLinkMatch.Groups[1].Value -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "ERROR: Could not find download button in HTML" -ForegroundColor Red
    exit 1
}

# Test the replacement pattern
$pattern = '(<a href=")[^"]+(" \s+class="inline-flex items-center gap-2[^>]+>\s+<svg[^>]+>[\s\S]*?</svg>\s+הורד את ההתקנה כעת)'
$replacement = "`$1$downloadUrl`$2"

$updatedContent = $htmlContent -replace $pattern, $replacement

if ($updatedContent -ne $htmlContent) {
    Write-Host ""
    Write-Host "SUCCESS: Pattern replacement works!" -ForegroundColor Green
    
    # Find the new link to verify
    $newLinkMatch = [regex]::Match($updatedContent, '<a href="([^"]+)"\s+class="inline-flex items-center gap-2[^>]+>\s+<svg[^>]+>[\s\S]*?</svg>\s+הורד את ההתקנה כעת')
    
    if ($newLinkMatch.Success) {
        Write-Host "New download link would be:" -ForegroundColor Green
        Write-Host $newLinkMatch.Groups[1].Value -ForegroundColor Cyan
        
        if ($newLinkMatch.Groups[1].Value -eq $downloadUrl) {
            Write-Host ""
            Write-Host "✓ URL replacement is correct!" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "✗ URL replacement failed - URLs don't match" -ForegroundColor Red
        }
    }
    
    # Ask if user wants to see a preview
    Write-Host ""
    $preview = Read-Host "Show preview of changes? (y/n)"
    if ($preview -eq "y") {
        Write-Host ""
        Write-Host "=== BEFORE ===" -ForegroundColor Yellow
        Write-Host $currentLinkMatch.Value
        Write-Host ""
        Write-Host "=== AFTER ===" -ForegroundColor Green
        Write-Host $newLinkMatch.Value
    }
    
} else {
    Write-Host ""
    Write-Host "ERROR: Pattern did not match - no replacement occurred" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Test completed successfully!" -ForegroundColor Green
