# Monitor both index builds and periodically search for "כי ביצחק" to check if line 548 is found

$exe = "CSharpBackend\FtsLib\FtsLibTest\bin\Release\FtsLibTest.exe"
$nomergeDir = "CSharpBackend\FtsLib\FtsLibTest\bin\Release\index_500k_nomerge"
$mergeDir = "CSharpBackend\FtsLib\FtsLibTest\bin\Release\index_500k_merge"
$query = "כי ביצחק"
$tier = "500k"

function Test-Index {
    param([string]$indexDir, [string]$label)
    
    # Check if index exists and has segments
    if (-not (Test-Path $indexDir)) {
        Write-Host "[$label] Index directory does not exist yet"
        return $null
    }
    
    $segments = @(Get-ChildItem "$indexDir\seg_*.dat" -ErrorAction SilentlyContinue)
    if ($segments.Count -eq 0) {
        Write-Host "[$label] No segments found yet"
        return $null
    }
    
    Write-Host "[$label] Found $($segments.Count) segment(s), searching for query..."
    
    # Run search and capture output
    $output = & $exe query $tier $query 2>&1 | Out-String
    
    # Check if line 548 is in the results
    if ($output -match "\[548\]") {
        Write-Host "[$label] PASS: Line 548 FOUND in results"
        return $true
    } else {
        Write-Host "[$label] FAIL: Line 548 NOT found"
        return $false
    }
}

Write-Host "=== FTS Index Build Monitor ==="
Write-Host "Monitoring builds and searching for כי ביצחק periodically"
Write-Host ""

$iteration = 0
while ($true) {
    $iteration++
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] Iteration $iteration"
    
    # Test nomerge index
    $nomergeResult = Test-Index $nomergeDir "NOMERGE"
    
    # Test merge index (only if nomerge is done or has segments)
    $mergeResult = Test-Index $mergeDir "MERGE"
    
    Write-Host ""
    
    # If both indexes exist and have been tested, wait before next check
    if ($nomergeResult -ne $null -or $mergeResult -ne $null) {
        Write-Host "Waiting 60 seconds before next check..."
        Start-Sleep -Seconds 60
    } else {
        Write-Host "Waiting 30 seconds before next check..."
        Start-Sleep -Seconds 30
    }
}
