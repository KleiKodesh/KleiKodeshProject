# build-helpers.ps1 - dot-source this file to get shared paths and utilities
# Usage: . "$PSScriptRoot\build-helpers.ps1"

# -- Paths --------------------------------------------------------------------
$ScriptsDir          = $PSScriptRoot
$BuildDir            = Split-Path -Parent $ScriptsDir
$ProjectRoot         = Split-Path -Parent $BuildDir
$AddinInstallerPath  = Join-Path $BuildDir    "Installer\Helpers\AddinInstaller.cs"
$UpdateVersionScript = Join-Path $BuildDir    "Installer\UpdateVersion.ps1"
$WpfProjectPath      = Join-Path $BuildDir    "Installer\KleiKodeshVstoInstallerWpf.csproj"
$SolutionPath        = Join-Path $ProjectRoot "KleiKodeshProject.slnx"
$NsisScriptPath      = Join-Path $BuildDir    "nsis\KleiKodeshWrapper.nsi"
$ReleasesDir         = Join-Path $BuildDir    "releases"
$ReleaseNotesFile    = Join-Path $ProjectRoot "RELEASE_NOTES.txt"
$DemoAppProjectPath  = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\KitveiHakodeshDemoApp.csproj"
$DemoAppReleaseDir   = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\bin\Release"
# Separate git repo (kleikodesh.github.io), gitignored by this one. The release step
# rewrites its download link to the new version and pushes it.
$WebsiteRepo         = Join-Path $ProjectRoot "kleikodesh-website"

# -- Read current version from source -----------------------------------------
function Get-CurrentVersion {
    $m = Select-String -Path $AddinInstallerPath -Pattern 'const string Version\s*=\s*"([^"]+)"'
    if (-not $m) { throw "Cannot read version from AddinInstaller.cs" }
    return $m.Matches[0].Groups[1].Value
}

# -- Force a version into all three stamp targets -----------------------------
# Last-resort repair for when UpdateVersion.ps1 leaves the constant untouched (its
# regex can miss, and its auto-increment path derives from the GitHub tag rather than
# from the file). Same three targets and formats as UpdateVersion.ps1 -- see
# .kiro/steering/version-management.md, which is the contract for this list.
function Set-Version {
    param([Parameter(Mandatory)][string]$Version)   # "vX.Y.Z"

    $numeric = $Version -replace '^v', ''
    $utf8    = New-Object System.Text.UTF8Encoding($false)   # no BOM

    # 1. AddinInstaller.cs -- the source of truth.
    $c = [System.IO.File]::ReadAllText($AddinInstallerPath, $utf8)
    $c = $c -replace '((?:public\s+)?const\s+string\s+Version\s*=\s*)"v[^"]*"', "`$1`"$Version`""
    [System.IO.File]::WriteAllText($AddinInstallerPath, $c, $utf8)

    # 2. Installer csproj <Version> -- numeric, no "v".
    if (Test-Path $WpfProjectPath) {
        $p = [System.IO.File]::ReadAllText($WpfProjectPath, $utf8)
        $p = $p -replace '<Version>[^<]*</Version>', "<Version>$numeric</Version>"
        [System.IO.File]::WriteAllText($WpfProjectPath, $p, $utf8)
    }

    # 3. KitveiHakodesh app exe -- old-style csproj, so the version lives in
    # AssemblyInfo.cs. This file is UTF-8 WITH BOM and the BOM must survive: without it
    # the compiler reads its Hebrew AssemblyTitle/AssemblyProduct literals as ANSI.
    $info = Join-Path $ProjectRoot "KitveiHakodesh\CSharpBackend\KitveiHakodeshDemoApp\Properties\AssemblyInfo.cs"
    if (Test-Path $info) {
        $utf8Bom = New-Object System.Text.UTF8Encoding($true)
        $pe = "$numeric.0"                       # AssemblyVersion needs four parts
        $a  = [System.IO.File]::ReadAllText($info, $utf8Bom)
        $a  = $a `
            -replace '(\[assembly:\s*AssemblyVersion\(")[^"]*("\)\])',     "`${1}$pe`${2}" `
            -replace '(\[assembly:\s*AssemblyFileVersion\(")[^"]*("\)\])', "`${1}$pe`${2}"
        [System.IO.File]::WriteAllText($info, $a, $utf8Bom)
    }
}

# -- Locate MSBuild -----------------------------------------------------------
function Find-MSBuild {
    $inPath = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }
    @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    ) | ForEach-Object { if (Test-Path $_) { return $_ } }
    return $null
}

# -- Clean solution -----------------------------------------------------------
function Invoke-SolutionClean {
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    $msbuild = Find-MSBuild
    if ($msbuild) {
        & $msbuild $SolutionPath /t:Clean /p:Configuration=Release /p:Platform="Any CPU" /verbosity:minimal
        if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: MSBuild clean had issues, continuing." -ForegroundColor Yellow }
    } else {
        Write-Host "MSBuild not found - cleaning WPF project only via dotnet." -ForegroundColor Yellow
        dotnet clean $WpfProjectPath -c Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: dotnet clean had issues, continuing." -ForegroundColor Yellow }
    }
}

# -- Build release notes string -----------------------------------------------
function New-ReleaseNotes {
    param([string]$Version, [string]$Source)   # Source: commits | file | both

    $fileContent = if (Test-Path $ReleaseNotesFile) { Get-Content $ReleaseNotesFile -Raw } else { "" }

    $previousTag = gh release list --limit 1 --json tagName --jq '.[0].tagName' 2>$null
    $commits     = if ($previousTag -and $LASTEXITCODE -eq 0) {
                       git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" 2>$null
                   } else {
                       git log -10 --pretty=format:"- %s (%h)" 2>$null
                   }
    $commitBlock = if ($commits) {
                       $label = if ($previousTag) { "Commits since ${previousTag}" } else { "Recent commits" }
                       "**${label}:**`n$commits"
                   } else { "" }

    switch ($Source) {
        "commits" {
            return "Release $Version`n`n$commitBlock"
        }
        "file" {
            $prefix = if ($fileContent) { $fileContent + "`n`n" } else { "" }
            return "Release $Version`n`n$prefix"
        }
        "both" {
            $sep    = "`n`n---`n`n"
            $prefix = if ($fileContent) { $fileContent + $sep } else { "Release $Version`n`n" }
            return "$prefix$commitBlock"
        }
    }
}
