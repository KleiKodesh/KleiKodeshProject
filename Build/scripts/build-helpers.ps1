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

# -- Step timing --------------------------------------------------------------
# Start-BuildStep closes the previous step and opens the next, so every stage gets a
# timestamped header and its own elapsed time. Complete-BuildSteps closes the last one
# and prints where the time went. A failed build exits mid-step, so the last header
# printed is the step that failed.
$script:BuildSteps  = New-Object System.Collections.ArrayList
$script:CurrentStep = $null
$script:BuildWatch  = [System.Diagnostics.Stopwatch]::StartNew()

function Format-Elapsed([TimeSpan]$Time) {
    '{0}m{1:00}s' -f [int][math]::Floor($Time.TotalMinutes), $Time.Seconds
}

function Stop-CurrentBuildStep {
    if (-not $script:CurrentStep) { return }
    $step = $script:CurrentStep
    $step.Elapsed = $step.Watch.Elapsed
    Write-Host ("  ({0} took {1})" -f $step.Name, (Format-Elapsed $step.Elapsed)) -ForegroundColor DarkGray
    [void]$script:BuildSteps.Add($step)
    $script:CurrentStep = $null
}

function Start-BuildStep([string]$Name) {
    Stop-CurrentBuildStep
    Write-Host ""
    Write-Host ("[{0:HH:mm:ss}] >> {1}" -f (Get-Date), $Name) -ForegroundColor Yellow
    $script:CurrentStep = [pscustomobject]@{
        Name    = $Name
        Watch   = [System.Diagnostics.Stopwatch]::StartNew()
        Elapsed = $null
    }
}

function Complete-BuildSteps {
    Stop-CurrentBuildStep
    Write-Host ""
    Write-Host "Time per step:" -ForegroundColor Cyan
    foreach ($step in $script:BuildSteps) {
        Write-Host ("  {0,8}  {1}" -f (Format-Elapsed $step.Elapsed), $step.Name)
    }
    Write-Host ("  {0,8}  total" -f (Format-Elapsed $script:BuildWatch.Elapsed)) -ForegroundColor Cyan
}

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

# -- Add-in signing certificate ---------------------------------------------
# The VSTO manifests must be signed, but any key will do: the installer registers the
# manifest's own public key as trusted on every install. The key is a self-signed
# temporary one (VS "Create Test Certificate"), made fresh on EVERY build, locally and
# in CI: a release's certificate then always expires 5 years after the release, which
# the release workflow's gate relies on to publish a renewal release in time.
# Updates the pfx (gitignored) and the thumbprint in KleiKodeshVsto.csproj.
function Update-SigningCertificate {
    $pfx  = Join-Path $ProjectRoot "KleiKodeshVsto\KleiKodesh_TemporaryKey.pfx"
    $proj = Join-Path $ProjectRoot "KleiKodeshVsto\KleiKodeshVsto.csproj"

    $cert = New-SelfSignedCertificate -Subject "CN=KleiKodesh" -Type CodeSigningCert `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (New-Object System.Security.SecureString) | Out-Null

    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $c = [System.IO.File]::ReadAllText($proj, $utf8)
    $c = $c -replace '<ManifestCertificateThumbprint>[^<]*<', "<ManifestCertificateThumbprint>$($cert.Thumbprint)<"
    [System.IO.File]::WriteAllText($proj, $c, $utf8)
    Write-Host "New signing certificate $($cert.Thumbprint), valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Green
}

# -- Locate MSBuild -----------------------------------------------------------
function Find-MSBuild {
    # foreach statement, not ForEach-Object: `return` inside a ForEach-Object block only
    # ends that iteration, so the function emitted an array (path + $null). Invoking that
    # array with & runs nothing and leaves $LASTEXITCODE at 0 - the clean and portable
    # builds were silently skipped.
    $inPath = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $candidates) { if (Test-Path $candidate) { return $candidate } }
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
