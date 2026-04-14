# fetch-wiktionary-api.ps1
# Fetches all Hebrew Wiktionary pages via the MediaWiki API and saves them
# as data/hewiktionary-pages.jsonl (one JSON object per line: {title, wikitext}).
# Only saves pages with Hebrew titles that have at least one definition line.
# Resumable — re-run to continue from where it left off.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts/fetch-wiktionary-api.ps1

$API   = "https://he.wiktionary.org/w/api.php"
$OUT   = "data/hewiktionary-pages.jsonl"
$META  = "data/hewiktionary-meta.json"
$BATCH = 50   # pages per wikitext fetch (API max for revisions)

if (-not (Test-Path "data")) { New-Item -ItemType Directory -Path "data" | Out-Null }

# ── Resume support ────────────────────────────────────────────────────────────
$meta = @{ apfrom = ""; saved = 0; skipped = 0 }
if (Test-Path $META) {
    try { $meta = Get-Content $META -Raw | ConvertFrom-Json } catch {}
    Write-Host "Resuming from '$($meta.apfrom)' — $($meta.saved) saved, $($meta.skipped) skipped"
} else {
    Write-Host "Starting fresh..."
}

$writer = [System.IO.StreamWriter]::new($OUT, $true, [System.Text.Encoding]::UTF8)
$saved   = [int]$meta.saved
$skipped = [int]$meta.skipped
$apfrom  = $meta.apfrom
$keepGoing = $true

function Save-Meta {
    @{ apfrom = $apfrom; saved = $saved; skipped = $skipped } | ConvertTo-Json | Set-Content $META -Encoding UTF8
}

function Fetch-Url($url) {
    $retries = 3
    while ($retries -gt 0) {
        try {
            return (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 45).Content
        } catch {
            $retries--
            if ($retries -eq 0) { throw }
            Start-Sleep 3
        }
    }
}

# ── Main loop ─────────────────────────────────────────────────────────────────
while ($keepGoing) {
    # 1. Get a page of titles (500 at a time)
    $listUrl = $API + "?action=query&list=allpages&apnamespace=0&aplimit=500&format=json"
    if ($apfrom -ne "") { $listUrl += "&apfrom=" + [Uri]::EscapeDataString($apfrom) }

    $listData = $null
    try {
        $listData = Fetch-Url $listUrl | ConvertFrom-Json
    } catch {
        Write-Warning "allpages failed: $($_.Exception.Message) — retrying in 10s"
        Start-Sleep 10
        continue
    }

    # Filter to Hebrew-titled pages only
    $allTitles = @($listData.query.allpages | ForEach-Object { $_.title } | Where-Object {
        $hasHeb = $false
        foreach ($c in $_.ToCharArray()) {
            $cp = [int][char]$c
            if ($cp -ge 1488 -and $cp -le 1514) { $hasHeb = $true; break }
        }
        $hasHeb
    })

    # Check for continuation
    $nextApfrom = ""
    if ($listData.PSObject.Properties['continue'] -and $listData.continue.PSObject.Properties['apcontinue']) {
        $nextApfrom = $listData.continue.apcontinue
    } else {
        $keepGoing = $false
    }

    # 2. Fetch wikitext in batches of $BATCH
    $i = 0
    while ($i -lt $allTitles.Count) {
        $end   = [Math]::Min($i + $BATCH - 1, $allTitles.Count - 1)
        $chunk = $allTitles[$i..$end]
        $encodedTitles = @()
        foreach ($t in $chunk) { $encodedTitles += [Uri]::EscapeDataString($t) }
        $titlesParam = $encodedTitles -join "|"
        $wtUrl = $API + "?action=query&titles=$titlesParam&prop=revisions&rvprop=content&rvslots=main&format=json"

        $wtData = $null
        try {
            $wtData = Fetch-Url $wtUrl | ConvertFrom-Json
        } catch {
            Write-Warning "wikitext fetch failed: $($_.Exception.Message) — skipping batch"
            $i += $BATCH
            continue
        }

        foreach ($prop in $wtData.query.pages.PSObject.Properties) {
            $page = $prop.Value
            if ($page.PSObject.Properties['missing']) { $skipped++; continue }

            $wikitext = $null
            if ($page.PSObject.Properties['revisions'] -and $page.revisions.Count -gt 0) {
                $rev = $page.revisions[0]
                if ($rev.PSObject.Properties['slots']) { $wikitext = $rev.slots.main.'*' }
                if (-not $wikitext -and $rev.PSObject.Properties['*']) { $wikitext = $rev.'*' }
            }
            if (-not $wikitext) { $skipped++; continue }

            # Skip redirects and pages with no definition lines
            if ($wikitext -match '^#' -or $wikitext.Length -lt 30) { $skipped++; continue }
            if (-not ($wikitext -match '(?m)^#[^#!]')) { $skipped++; continue }

            # Escape for JSON (manual — avoids ConvertTo-Json depth/encoding issues)
            $te = $page.title -replace '\\','\\' -replace '"','\"'
            $we = $wikitext -replace '\\','\\' -replace '"','\"'
            $we = $we -replace "`r`n",'\n' -replace "`n",'\n' -replace "`r",'\n'
            $writer.WriteLine('{"title":"' + $te + '","wikitext":"' + $we + '"}')
            $saved++
        }

        $i += $BATCH
        Write-Host "`r  $saved saved, $skipped skipped | last: $($allTitles[$end].Substring(0,[Math]::Min(15,$allTitles[$end].Length)))" -NoNewline
        Start-Sleep -Milliseconds 100
    }

    # Save resume position after each 500-title page
    $apfrom = $nextApfrom
    Save-Meta
}

$writer.Close()
Write-Host ""
Write-Host "Done! Saved: $saved  Skipped: $skipped"
Write-Host "Output: $OUT"
