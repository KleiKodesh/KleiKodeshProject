param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$backendRoot = Join-Path $repoRoot 'Kezayit\CSharpBackend'
$solutionPath = Join-Path $backendRoot 'Kezayit.slnx'
$libPath = Join-Path $backendRoot 'KezayitLib\KezayitLib.csproj'
$searchPath = Join-Path $backendRoot 'BloomSearchEngine\BloomSearchEngineLib\BloomSearchEngineLib.csproj'
$demoPath = Join-Path $backendRoot 'KezayitDemoApp\KezayitDemoApp.csproj'

function Write-Info([string]$Message) {
    if (-not $Quiet) {
        Write-Host $Message -ForegroundColor Cyan
    }
}

function Find-MSBuild {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $vswhere = "$env:ProgramFiles(x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($installPath) {
            $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    $candidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles(x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles(x86)\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles(x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles(x86)\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

if (-not (Test-Path $solutionPath)) {
    throw "Could not find backend solution at $solutionPath"
}

$msbuild = Find-MSBuild
if (-not $msbuild) {
    throw @"
MSBuild was not found.

Install the Visual Studio Build Tools with:
- MSBuild
- .NET Framework 4.8 build support

After that, rerun this script.
"@
}

Write-Info "Building Kezayit backend in Release..."
Write-Info "MSBuild: $msbuild"
Write-Info "Solution: $solutionPath"

$commonArgs = @(
    '/p:Configuration=Release'
    '/p:Platform=Any CPU'
    '/m'
    '/verbosity:minimal'
)

Push-Location $backendRoot
try {
    & $msbuild $solutionPath '/t:Rebuild' @commonArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Info "Backend rebuild finished successfully."
        exit 0
    }

    Write-Host "Solution rebuild did not complete cleanly. Falling back to the individual projects..." -ForegroundColor Yellow

    & $msbuild $searchPath '/t:Rebuild' @commonArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $msbuild $libPath '/t:Rebuild' @commonArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $msbuild $demoPath '/t:Rebuild' @commonArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Info "Backend rebuild finished successfully."
    exit 0
}
finally {
    Pop-Location
}
