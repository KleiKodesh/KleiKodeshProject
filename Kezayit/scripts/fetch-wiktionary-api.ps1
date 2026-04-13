# fetch-wiktionary-api.ps1
# Downloads all Hebrew Wiktionary pages via the MediaWiki API and saves them
# as a JSONL file (one JSON object per line: { title, wikitext }).
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts/fetch-wiktionary-api.ps1

$API = "https://he.wiktionary.org/w/api.php"
$OUT = "data/hewiktionary-pages.jsonl"
$BATCH = 50

if (-not (Test-Path "data")) { New-Item -ItemType Directory -Path "data" | Out-Null }

# Resume support
$existingTitles = @{}
if (Test-Path $OUT) {
    $lines = Get-Content $OUT -Encoding UTF8
    foreach ($line in $lines) {
        try {
            $obj = $line | ConvertFrom-Json
            $existingTitles[$obj.title] = $true
        } catch {}
    }
    Write-Host "Resuming - $($existingTitles.Count) pages already saved"
}

$writer = [System.IO.StreamWriter]::new($OUT, $true, [System.Text.Encoding]::UTF8)

$totalFetched = 0
$apcontinue = ""
$pageNum = 0
$keepGoing = $true

while ($keepGoing) {
    $url = "$API`?action=query&list=allpages&apnamespace=0&aplimit=500&format=json"
    if ($apcontinue -ne "") {
        $url += "&apcontinue=$([Uri]::EscapeDataString($apcontinue))"
    }

    $data = $null
    $retries = 3
    while ($retries -gt 0) {
        try {
            $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
            $data = $resp.Content | ConvertFrom-Json
            break
        } catch {
            $retries--
            Write-Warning "allpages fetch failed: $($_.Exception.Message) - retrying..."
            Start-Sleep 5
        }
    }
    if ($null -eq $data) { Write-Error "Failed to fetch allpages"; break }

    $titles = $data.query.allpages | ForEach-Object { $_.title }

    # Check for continuation
    if ($data.PSObject.Properties['continue'] -and $data.continue.PSObject.Properties['apcontinue']) {
        $apcontinue = $data.continue.apcontinue
    } else {
        $keepGoing = $false
    }

    # Filter to Hebrew-only titles not already saved
    $toFetch = @($titles | Where-Object { $_ -match "[\u05D0-\u05EA]" -and -not $existingTitles.ContainsKey($_) })

    # Batch-fetch wikitext
    $i = 0
    while ($i -lt $toFetch.Count) {
        $end = [Math]::Min($i + $BATCH - 1, $toFetch.Count - 1)
        $chunk = $toFetch[$i..$end]
        $titlesParam = ($chunk | ForEach-Object { [Uri]::EscapeDataString($_) }) -join "|"
        $wtUrl = "$API`?action=query&titles=$titlesParam&prop=revisions&rvprop=content&rvslots=main&format=json"

        $wtData = $null
        $r2 = 3
        while ($r2 -gt 0) {
            try {
                $wtResp = Invoke-WebRequest -Uri $wtUrl -UseBasicParsing -TimeoutSec 60
                $wtData = $wtResp.Content | ConvertFrom-Json
                break
            } catch {
                $r2--
                Start-Sleep 3
            }
        }

        if ($null -ne $wtData) {
            foreach ($prop in $wtData.query.pages.PSObject.Properties) {
                $page = $prop.Value
                if ($page.PSObject.Properties['missing']) { continue }
                $wikitext = $null
                if ($page.PSObject.Properties['revisions'] -and $page.revisions.Count -gt 0) {
                    $rev = $page.revisions[0]
                    if ($rev.PSObject.Properties['slots']) {
                        $wikitext = $rev.slots.main.'*'
                    }
                    if (-not $wikitext -and $rev.PSObject.Properties['*']) {
                        $wikitext = $rev.'*'
                    }
                }
                if (-not $wikitext) { continue }

                # Escape for JSON manually to avoid ConvertTo-Json depth issues
                $titleEsc = $page.title -replace '\\', '\\' -replace '"', '\"'
                $wtEsc = $wikitext -replace '\\', '\\' -replace '"', '\"' -replace "`r`n", '\n' -replace "`n", '\n' -replace "`r", '\n'
                $json = '{"title":"' + $titleEsc + '","wikitext":"' + $wtEsc + '"}'
                $writer.WriteLine($json)
                $existingTitles[$page.title] = $true
                $totalFetched++
            }
        }

        $i += $BATCH
        $pageNum += $chunk.Count
        Write-Host "`r  $pageNum titles, $totalFetched saved..." -NoNewline
        Start-Sleep -Milliseconds 50
    }
}

$writer.Close()
Write-Host ""
Write-Host "Done. Total pages saved: $totalFetched"
Write-Host "Output: $OUT"
