param(
    [string]$FilePath,
    [ValidateSet("major", "minor", "patch")]
    [string]$IncrementType = "patch"
)

# Derive sibling paths from $FilePath (which points to AddinInstaller.cs)
$projectDir   = Split-Path -Parent (Split-Path -Parent $FilePath)   # KleiKodeshVstoInstallerWpf/
$csprojPath   = Join-Path $projectDir "KleiKodeshVstoInstallerWpf.csproj"

function Update-AllVersionTargets($newVersion) {
    # Strip leading 'v' for numeric-only contexts (csproj <Version>)
    $numericVersion = $newVersion -replace '^v', ''

    # 1. AddinInstaller.cs — const string Version = "vX.Y.Z";
    $content = Get-Content $FilePath -Raw
    $updated = $content -replace '(const string Version\s*=\s*)"v[^"]*"', "`$1`"$newVersion`""
    if ($updated -ne $content) {
        Set-Content $FilePath -Value $updated -NoNewline
        Write-Host "  [OK] AddinInstaller.cs -> $newVersion"
    } else {
        Write-Host "  [WARN] AddinInstaller.cs: pattern not matched, no change"
    }

    # 2. KleiKodeshVstoInstallerWpf.csproj — <Version>X.Y.Z</Version>
    #    Insert or update inside the first <PropertyGroup>
    if (Test-Path $csprojPath) {
        $csproj = Get-Content $csprojPath -Raw
        if ($csproj -match '<Version>[^<]*</Version>') {
            $csproj = $csproj -replace '<Version>[^<]*</Version>', "<Version>$numericVersion</Version>"
        } else {
            # Insert after <UseWPF>true</UseWPF> or after first <OutputType> line
            $csproj = $csproj -replace '(<UseWPF>true</UseWPF>)', "`$1`n    <Version>$numericVersion</Version>"
        }
        Set-Content $csprojPath -Value $csproj -NoNewline
        Write-Host "  [OK] KleiKodeshVstoInstallerWpf.csproj -> $numericVersion"
    } else {
        Write-Host "  [SKIP] csproj not found at $csprojPath"
    }
}

try {
    Write-Host "Getting latest version from GitHub..."

    $latestVersion = (Invoke-RestMethod `
        -Uri 'https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest' `
        -Headers @{'User-Agent'='KleiKodesh-BuildScript'}).tag_name

    if ($latestVersion -match '^v(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$matches[1]
        $minor = [int]$matches[2]
        $patch = [int]$matches[3]

        switch ($IncrementType) {
            "major" { $major++; $minor = 0; $patch = 0; Write-Host "Incrementing MAJOR version (breaking changes)" }
            "minor" { $minor++;             $patch = 0; Write-Host "Incrementing MINOR version (new features)"    }
            "patch" { $patch++;                         Write-Host "Incrementing PATCH version (bug fixes)"       }
        }

        $newVersion = "v$major.$minor.$patch"
    } else {
        Write-Host "Warning: Unexpected version format '$latestVersion', using v1.0.16"
        $newVersion = "v1.0.16"
    }

    Write-Host "Latest GitHub version: $latestVersion"
    Write-Host "New version for this build: $newVersion"

    Update-AllVersionTargets $newVersion

    Write-Host "Version updated successfully to $newVersion"

} catch {
    Write-Host "Error updating version: $_"
    Write-Host "Using fallback version v1.0.16"
    Update-AllVersionTargets "v1.0.16"
}
