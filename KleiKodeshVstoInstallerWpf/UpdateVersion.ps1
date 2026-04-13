param(
    [string]$FilePath,
    [ValidateSet("major", "minor", "patch")]
    [string]$IncrementType = "patch"
)

try {
    Write-Host "Getting latest version from GitHub..."
    
    # Get the latest release version
    $latestVersion = (Invoke-RestMethod -Uri 'https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest' -Headers @{'User-Agent'='KleiKodesh-BuildScript'}).tag_name
    
    # Parse version and increment based on type
    if ($latestVersion -match '^v(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$matches[1]
        $minor = [int]$matches[2] 
        $patch = [int]$matches[3]
        
        switch ($IncrementType) {
            "major" {
                $major++
                $minor = 0
                $patch = 0
                Write-Host "Incrementing MAJOR version (breaking changes)"
            }
            "minor" {
                $minor++
                $patch = 0
                Write-Host "Incrementing MINOR version (new features)"
            }
            "patch" {
                $patch++
                Write-Host "Incrementing PATCH version (bug fixes)"
            }
        }
        
        $newVersion = "v$major.$minor.$patch"
    } else {
        # Fallback if version format is unexpected
        Write-Host "Warning: Unexpected version format '$latestVersion', using v1.0.16"
        $newVersion = "v1.0.16"
    }
    
    Write-Host "Latest GitHub version: $latestVersion"
    Write-Host "New version for this build: $newVersion"
    
    # Update the version in the file
    $content = Get-Content $FilePath -Raw
    $newContent = $content -replace '(const string Version\s*=\s*)"v[^"]*"', "`$1`"$newVersion`""
    Set-Content $FilePath -Value $newContent -NoNewline
    
    Write-Host "Version updated successfully to $newVersion"
} catch {
    Write-Host "Error updating version: $_"
    Write-Host "Using fallback version v1.0.16"
    
    # Fallback version update
    $content = Get-Content $FilePath -Raw
    $newContent = $content -replace '(const string Version\s*=\s*)"v[^"]*"', '$1"v1.0.16"'
    Set-Content $FilePath -Value $newContent -NoNewline
}