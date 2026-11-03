<#
.SYNOPSIS
    Downloads the latest released Dictionary.db and expansion-routed.db to the paths the
    csproj files expect. The ONE place that knows where these DBs come from.

.DESCRIPTION
    Sources (GitHub Releases, published by each repo's CI on every push):
      Dictionary.db         KleiKodesh/KitveiHakodeshDictionary
      expansion-routed.db   KleiKodesh/SearchExpansion
    Each download is checked against the release's <name>.sha256 asset. The release tag
    in use is kept next to the file as <file>.release-tag (gitignored).

    -Mode Always   resolve the latest tag every time; any failure is an error
                   (installer builds: build-installer.ps1, release.yml).
    -Mode IfStale  for plain developer builds (MSBuild target in FetchReleaseDbs.targets):
                   skips the network if the file exists and was checked < 6 h ago
                   (<file>.release-checked); a
                   network failure with an existing file is only a warning.

    Used by: Build/scripts/build-installer.ps1 (local + GitHub release.yml),
    KitveiHakodesh/CSharpBackend/FetchReleaseDbs.targets (KitveiHakodesh.Core.csproj,
    FtsLib.csproj -> every app/portable build). Works on Windows PowerShell 5.1 and pwsh.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Build\scripts\fetch-release-dbs.ps1 -Mode Always
#>
param(
    [ValidateSet('Always', 'IfStale')] [string]$Mode = 'Always',
    [ValidateSet('all', 'dictionary', 'expansion')] [string]$Only = 'all',
    [double]$StaleHours = 6
)
$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dbs = @(
    @{ Key = 'dictionary'; Repo = 'KleiKodesh/KitveiHakodeshDictionary'; Asset = 'Dictionary.db'
       Dest = 'KitveiHakodesh\CSharpBackend\KitveiHakodesh.Core\Dictionary\Dictionary.db' },
    @{ Key = 'expansion';  Repo = 'KleiKodesh/SearchExpansion';          Asset = 'expansion-routed.db'
       Dest = 'KitveiHakodesh\CSharpBackend\FtsLib-Csharp\SearchExpansion\expansion-routed.db' }
)

function Get-LatestTag([string]$repo) {
    # /releases/latest answers with a redirect to /releases/tag/<tag>: no API, no rate limit.
    $req = [Net.HttpWebRequest]::Create("https://github.com/$repo/releases/latest")
    $req.AllowAutoRedirect = $false
    $req.Method = 'HEAD'
    $req.Timeout = 15000
    $req.UserAgent = 'KleiKodesh-build'
    $resp = $req.GetResponse()
    try { $loc = $resp.Headers['Location'] } finally { $resp.Close() }
    if (-not $loc -or $loc -notmatch '/releases/tag/([^/?#]+)') { throw "no release found for $repo" }
    return [Uri]::UnescapeDataString($Matches[1])
}

function Get-File([string]$url, [string]$path) {
    $wc = New-Object Net.WebClient
    $wc.Headers['User-Agent'] = 'KleiKodesh-build'
    try { $wc.DownloadFile($url, $path) } finally { $wc.Dispose() }
}

# Parallel builds (multi-targeted FtsLib, several projects) call this at once: one at a time.
$mutex = New-Object Threading.Mutex($false, 'KleiKodeshFetchReleaseDbs')
try { [void]$mutex.WaitOne() } catch [Threading.AbandonedMutexException] {}

$failed = $false
foreach ($db in $dbs) {
    if ($Only -ne 'all' -and $Only -ne $db.Key) { continue }
    $dest = Join-Path $root $db.Dest
    $tagFile = "$dest.release-tag"
    $checkFile = "$dest.release-checked"   # touched after every check, even a failed one
    $have = Test-Path $dest
    $haveTag = if (Test-Path $tagFile) { (Get-Content $tagFile -Raw).Trim() } else { '' }

    if ($Mode -eq 'IfStale' -and $have -and (Test-Path $checkFile) -and
        ((Get-Date) - (Get-Item $checkFile).LastWriteTime).TotalHours -lt $StaleHours) {
        Write-Host "[fetch-release-dbs] $($db.Asset): $haveTag (checked < $StaleHours h ago, skipped)"
        continue
    }
    Set-Content -Path $checkFile -Value (Get-Date -Format o) -Encoding ascii -ErrorAction SilentlyContinue
    try {
        Write-Host "[fetch-release-dbs] $($db.Asset): resolving latest release of $($db.Repo)..."
        $tag = Get-LatestTag $db.Repo
        if ($have -and $tag -eq $haveTag) {
            Write-Host "[fetch-release-dbs] $($db.Asset): up to date ($tag)"
            continue
        }
        $base = "https://github.com/$($db.Repo)/releases/download/$tag"
        $tmp = "$dest.download"
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Write-Host "[fetch-release-dbs] $($db.Asset): downloading $tag..."
        Get-File "$base/$($db.Asset)" $tmp
        Get-File "$base/$($db.Asset).sha256" "$tmp.sha256"
        $expected = ((Get-Content "$tmp.sha256" -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
        $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        Remove-Item "$tmp.sha256" -Force
        if ($actual -ne $expected) {
            Remove-Item $tmp -Force
            throw "sha256 mismatch for $($db.Asset) $tag (got $actual, release says $expected)"
        }
        Move-Item $tmp $dest -Force
        Set-Content -Path $tagFile -Value $tag -Encoding ascii
        Write-Host "[fetch-release-dbs] $($db.Asset): $tag ($((Get-Item $dest).Length) bytes, sha256 ok)"
    }
    catch {
        if ($Mode -eq 'IfStale' -and $have) {
            Write-Warning "[fetch-release-dbs] $($db.Asset): could not check for a newer release ($($_.Exception.Message)); using the existing file."
        }
        else {
            Write-Host "[fetch-release-dbs] ERROR $($db.Asset): $($_.Exception.Message)" -ForegroundColor Red
            $failed = $true
        }
    }
}
$mutex.ReleaseMutex()
if ($failed) { exit 1 }
exit 0
