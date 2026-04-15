# build-helpers.ps1 - dot-source this file to get shared paths and utilities
# Usage: . "$PSScriptRoot\build-helpers.ps1"

# -- Paths --------------------------------------------------------------------
$BuildDir            = $PSScriptRoot
$ProjectRoot         = Split-Path -Parent $BuildDir
$AddinInstallerPath  = Join-Path $ProjectRoot "KleiKodeshVstoInstallerWpf\Helpers\AddinInstaller.cs"
$UpdateVersionScript = Join-Path $ProjectRoot "KleiKodeshVstoInstallerWpf\UpdateVersion.ps1"
$WpfProjectPath      = Join-Path $ProjectRoot "KleiKodeshVstoInstallerWpf\KleiKodeshVstoInstallerWpf.csproj"
$SolutionPath        = Join-Path $ProjectRoot "KleiKodeshProject.slnx"
$NsisScriptPath      = Join-Path $BuildDir    "KleiKodeshWrapper.nsi"
$ReleasesDir         = Join-Path $BuildDir    "releases"
$ReleaseNotesFile    = Join-Path $ProjectRoot "RELEASE_NOTES.txt"

# -- Read current version from source -----------------------------------------
function Get-CurrentVersion {
    $m = Select-String -Path $AddinInstallerPath -Pattern 'const string Version\s*=\s*"([^"]+)"'
    if (-not $m) { throw "Cannot read version from AddinInstaller.cs" }
    return $m.Matches[0].Groups[1].Value
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

    $buildInfo   = "**Build:** Release|AnyCPU"
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
            return "Release $Version`n`n$buildInfo`n`n$commitBlock"
        }
        "file" {
            $prefix = if ($fileContent) { $fileContent + "`n`n" } else { "" }
            return "Release $Version`n`n$prefix$buildInfo"
        }
        "both" {
            $sep    = "`n`n---`n`n"
            $prefix = if ($fileContent) { $fileContent + $sep } else { "Release $Version`n`n" }
            return "$prefix$buildInfo`n`n$commitBlock"
        }
    }
}
