param(
    [string]$VueProjectPath,
    [string]$OutputPath
)

$cssFile = Join-Path $VueProjectPath "dist\style.css"
$jsFile = Join-Path $VueProjectPath "dist\index.js"
$outputFile = Join-Path $OutputPath "regexfind-index.html"

if ((Test-Path $cssFile) -and (Test-Path $jsFile)) {
    $css = Get-Content $cssFile -Raw -Encoding UTF8
    $js = Get-Content $jsFile -Raw -Encoding UTF8
    
    $html = @"
<!DOCTYPE html>
<html lang="he" dir="rtl">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>KleiKodesh Regex Find</title>
    <style>
$css
    </style>
</head>
<body>
    <div id="app"></div>
    <script type="module">
$js
    </script>
</body>
</html>
"@
    
    # Ensure directory exists
    $outputDir = Split-Path $outputFile -Parent
    if (!(Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    [System.IO.File]::WriteAllText($outputFile, $html, [System.Text.Encoding]::UTF8)
    Write-Host "Created single HTML file with inlined CSS and JS at: $outputFile"
    Write-Host "File size: $((Get-Item $outputFile).Length) bytes"
} else {
    Write-Warning "CSS or JS file not found, skipping HTML creation"
    Write-Host "CSS file: $cssFile (exists: $(Test-Path $cssFile))"
    Write-Host "JS file: $jsFile (exists: $(Test-Path $jsFile))"
}