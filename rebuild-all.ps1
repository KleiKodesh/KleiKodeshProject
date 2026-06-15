param(
    [switch]$Debug,
    [switch]$Release
)

if (-not $Debug -and -not $Release) {
    $Debug = $true
    $Release = $true
}

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

# Correct build order: dependencies first, then VSTO last
$projects = @(
    # Shared libraries (no dependencies)
    "WpfLib/WpfLib.csproj",
    "UpdateCheckerLib/UpdateCheckerLib.csproj",
    
    # DocDesign (depends on WpfLib)
    "KleiKodeshVsto/DocDesign/DocDesignLib/DocDesignLib.csproj",
    "KleiKodeshVsto/DocDesign/DocDesignDemo/DocDesignDemo.csproj",
    
    # RegexFind (depends on WpfLib)
    "KleiKodeshVsto/RegexInWord/RegexFindLib/RegexFindLib.csproj",
    "KleiKodeshVsto/RegexInWord/RegexFindDemo/RegexFindDemo.csproj",
    
    # WebSites (depends on WpfLib)
    "KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesLib.csproj",
    "KleiKodeshVsto/WebSitesLib/WebSitesDemo/WebSitesDemo.csproj",
    
    # Kiwix (no WpfLib dependency)
    "KleiKodeshVsto/Kiwix/KiwixLib/KiwixLib.csproj",
    "KleiKodeshVsto/Kiwix/KiwixDemoApp/KiwixDemoApp.csproj",
    
    # Nakdan (depends on WpfLib)
    "KleiKodeshVsto/Nakdan/Nakdan/Nakdan.csproj",
    "KleiKodeshVsto/Nakdan/NakdanDemo/NakdanDemo.csproj",
    
    # KitveiHakodesh backend
    "KitveiHakodesh/CSharpBackend/Ftslib-Csharp/FtsLib/FtsLib.csproj",
    "KitveiHakodesh/CSharpBackend/Ftslib-Csharp/FtsLibDemo/FtsLibDemo.csproj",
    "KitveiHakodesh/CSharpBackend/Ftslib-Csharp/FtsLibTest/FtsLibTest.csproj",
    "KitveiHakodesh/CSharpBackend/DocumentLocator/DocumentLocator/DocumentLocator.csproj",
    "KitveiHakodesh/CSharpBackend/DocumentLocator/DocumentLocator.Client/DocumentLocator.Client.csproj",
    "KitveiHakodesh/CSharpBackend/DocumentLocator/DocumentLocator.Service/DocumentLocator.Service.csproj",
    "KitveiHakodesh/CSharpBackend/DocumentLocator/DocumentLocator.Demo/DocumentLocator.Demo.csproj",
    "KitveiHakodesh/CSharpBackend/DocumentLocator/DocumentLocator.Tests/DocumentLocator.Tests.csproj",
    "KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/KitveiHakodeshLib.csproj",
    "KitveiHakodesh/CSharpBackend/KitveiHakodeshDemoApp/KitveiHakodeshDemoApp.csproj",
    
    # Build tools (SvgToPng is standalone)
    "Build/scripts/SvgToPng/SvgToPng.csproj",
    
    # VSTO add-in LAST (depends on all libraries above)
    "KleiKodeshVsto/KleiKodeshVsto.csproj",
    
    # Installer LAST (depends on VSTO)
    "Build/Installer/KleiKodeshVstoInstallerWpf.csproj"
)

$configurations = @()
if ($Debug) { $configurations += "Debug" }
if ($Release) { $configurations += "Release" }

$totalProjects = $projects.Count
$totalBuilds = $totalProjects * $configurations.Count
$successCount = 0
$failureCount = 0
$failures = @()

Write-Host "`nKleiKodesh Solution - Rebuild All Projects" -ForegroundColor Cyan
Write-Host "Total projects: $totalProjects | Configurations: $($configurations -join ', ')" -ForegroundColor Yellow

$startTime = Get-Date

foreach ($config in $configurations) {
    Write-Host "`nBuilding in $config configuration..." -ForegroundColor Magenta
    
    $configStartTime = Get-Date
    $configSuccessCount = 0
    
    for ($i = 0; $i -lt $projects.Count; $i++) {
        $project = $projects[$i]
        $projectNum = $i + 1
        $projectName = Split-Path -Leaf $project
        
        Write-Host "  [$projectNum/$totalProjects] $projectName... " -NoNewline -ForegroundColor Gray
        
        & $msbuild $project /m /nologo /verbosity:minimal /p:Configuration=$config 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "OK" -ForegroundColor Green
            $successCount++
            $configSuccessCount++
        }
        else {
            Write-Host "FAIL" -ForegroundColor Red
            $failureCount++
            $failures += "$project ($config)"
        }
    }
    
    $configEndTime = Get-Date
    $configDuration = [math]::Round(($configEndTime - $configStartTime).TotalSeconds, 2)
    Write-Host "  $config result: $configSuccessCount/$totalProjects successful in $configDuration seconds" -ForegroundColor Cyan
}

$endTime = Get-Date
$totalDuration = [math]::Round(($endTime - $startTime).TotalSeconds, 2)

Write-Host "`nSummary:" -ForegroundColor Cyan
Write-Host "  Successful: $successCount/$totalBuilds" -ForegroundColor Green
Write-Host "  Failed: $failureCount/$totalBuilds" -ForegroundColor $(if ($failureCount -eq 0) { "Green" } else { "Red" })
Write-Host "  Duration: $totalDuration seconds" -ForegroundColor Cyan

if ($failures.Count -gt 0) {
    Write-Host "`nFailed builds:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
else {
    Write-Host "`nAll projects rebuilt successfully!" -ForegroundColor Green
    exit 0
}