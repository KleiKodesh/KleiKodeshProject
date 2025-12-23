param(
    [string]$FilePath
)

try {
    Write-Host "Getting latest version from GitHub..."
    
    # Get the latest release version
    $latestVersion = (Invoke-RestMethod -Uri 'https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest' -Headers @{'User-Agent'='KleiKodesh-BuildScript'}).tag_name
    
    # Parse version and increment patch number
    if ($latestVersion -match '^v(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$matches[1]
        $minor = [int]$matches[2] 
        $patch = [int]$matches[3] + 1
        $newVersion = "v$major.$minor.$patch"
    } else {
        # Fallback if version format is unexpected
        Write-Host "Warning: Unexpected version format '$latestVersion', using v1.0.32"
        $newVersion = "v1.0.32"
    }
    
    Write-Host "Latest version: $latestVersion"
    Write-Host "New version: $newVersion"
    
    # Update the version in the file
    $content = Get-Content $FilePath
    $newContent = $content -replace 'const string Version = "v[^"]*";', "const string Version = `"$newVersion`";"
    Set-Content $FilePath -Value $newContent
    
    Write-Host "Version updated successfully to $newVersion"
} catch {
    Write-Host "Error updating version: $_"
    exit 1
}