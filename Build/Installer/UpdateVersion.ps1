param(
    [string]$FilePath,
    [ValidateSet("major", "minor", "patch")]
    [string]$IncrementType = "patch",
    # When set, skip GitHub fetch and use this exact version (e.g. "v3.5.0")
    [string]$ManualVersion
)

# Derive sibling paths from $FilePath (which points to AddinInstaller.cs)
$projectDir   = Split-Path -Parent (Split-Path -Parent $FilePath)   # Build/Installer/
$csprojPath   = Join-Path $projectDir "KleiKodeshVstoInstallerWpf.csproj"

function Update-AllVersionTargets($newVersion) {
    # Strip leading 'v' for numeric-only contexts (csproj <Version>)
    $numericVersion = $newVersion -replace '^v', ''
    $encUtf8 = New-Object System.Text.UTF8Encoding($false)   # UTF-8, no BOM

    # 1. AddinInstaller.cs — public const string Version = "vX.Y.Z";
    $content = [System.IO.File]::ReadAllText($FilePath, $encUtf8)
    # Check if already at the target version (no-op case)
    if ($content -match [regex]::Escape("`"$newVersion`"")) {
        Write-Host "  [OK] AddinInstaller.cs -> $newVersion (already current)"
    } else {
        $updated = $content -replace '((?:public\s+)?const\s+string\s+Version\s*=\s*)"v[^"]*"', "`$1`"$newVersion`""
        if ($updated -ne $content) {
            [System.IO.File]::WriteAllText($FilePath, $updated, $encUtf8)
            Write-Host "  [OK] AddinInstaller.cs -> $newVersion"
        } else {
            Write-Host "  [WARN] AddinInstaller.cs: pattern not matched, no change"
            Write-Host "  [DEBUG] First 200 chars: $($content.Substring(0, [Math]::Min(200, $content.Length)))"
        }
    }

    # 2. KleiKodeshVstoInstallerWpf.csproj — <Version>X.Y.Z</Version>
    if (Test-Path $csprojPath) {
        $csproj = [System.IO.File]::ReadAllText($csprojPath, $encUtf8)
        if ($csproj -match '<Version>[^<]*</Version>') {
            $csproj = $csproj -replace '<Version>[^<]*</Version>', "<Version>$numericVersion</Version>"
        } else {
            $csproj = $csproj -replace '(<UseWPF>true</UseWPF>)', "`$1`n    <Version>$numericVersion</Version>"
        }
        [System.IO.File]::WriteAllText($csprojPath, $csproj, $encUtf8)
        Write-Host "  [OK] KleiKodeshVstoInstallerWpf.csproj -> $numericVersion"
    } else {
        Write-Host "  [SKIP] csproj not found at $csprojPath"
    }
}

try {
    # ── Manual version path ──────────────────────────────────────────────────
    if ($ManualVersion) {
        # Normalise: ensure leading 'v'
        if ($ManualVersion -notmatch '^v') { $ManualVersion = "v$ManualVersion" }

        if ($ManualVersion -notmatch '^v\d+\.\d+\.\d+$') {
            Write-Host "ERROR: ManualVersion '$ManualVersion' is not valid semver (expected vX.Y.Z)" -ForegroundColor Red
            exit 1
        }

        Write-Host "Using manually specified version: $ManualVersion" -ForegroundColor Cyan
        Update-AllVersionTargets $ManualVersion
        Write-Host "Version updated successfully to $ManualVersion"
        exit 0
    }

    # ── Auto-increment path ──────────────────────────────────────────────────
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
