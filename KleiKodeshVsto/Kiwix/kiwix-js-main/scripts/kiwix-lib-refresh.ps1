# Refresh the KiwixLib runtime output from the kiwix-js-main source build.
# Usage: npm run kiwix-lib-refresh

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path "$scriptDir\.."
$buildDir = Join-Path $projectRoot "dist"
$targetDir = Join-Path $projectRoot "..\KiwixLib\Kiwix.js"

Write-Host "Building Kiwix JS source in $projectRoot"
Push-Location $projectRoot
try {
    npm run build-src
}
finally {
    Pop-Location
}

if (-not (Test-Path $buildDir)) {
    Write-Error "Build output folder not found: $buildDir"
    exit 1
}

if (-not (Test-Path $targetDir)) {
    Write-Host "Creating target folder: $targetDir"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

Write-Host "Copying files from $buildDir to $targetDir"
Copy-Item -Path (Join-Path $buildDir '*') -Destination $targetDir -Recurse -Force

Write-Host "KiwixLib refresh complete. Output updated in: $targetDir"
