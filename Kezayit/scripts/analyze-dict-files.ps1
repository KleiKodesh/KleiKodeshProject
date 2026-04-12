$enc = [System.Text.Encoding]::GetEncoding(1255)

foreach ($f in @("dictionary1.txt","dictionary2.txt","dictionary3.txt","dictionary4.txt","FinalDictionary.txt")) {
    $path = "C:\Users\Admin\Documents\ToratEmetInstall\Dictionaries\$f"
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = $enc.GetString($bytes)
    $allLines = $text -split "`n"
    $entries = $allLines | Where-Object { $_ -match "=" -and $_ -notmatch "^//" -and $_.Trim() -ne "" }
    Write-Output "=== $f === size=$([math]::Round($bytes.Length/1024))KB entries=$($entries.Count)"
}

# Deep analysis of FinalDictionary
$bytes = [System.IO.File]::ReadAllBytes("C:\Users\Admin\Documents\ToratEmetInstall\Dictionaries\FinalDictionary.txt")
$text = $enc.GetString($bytes)
$lines = $text -split "`n" | Where-Object { $_ -match "=" -and $_ -notmatch "^//" -and $_.Trim() -ne "" }

$s0 = ($lines | Where-Object { $_ -match "^0 " }).Count
$s1 = ($lines | Where-Object { $_ -match "^1 " }).Count
$s2 = ($lines | Where-Object { $_ -match "^2 " }).Count
$s3 = ($lines | Where-Object { $_ -match "^3 " }).Count
$noPrefix = ($lines | Where-Object { $_ -notmatch "^[0-3] " }).Count

Write-Output "FinalDictionary breakdown:"
Write-Output "  prefix 0: $s0"
Write-Output "  prefix 1: $s1"
Write-Output "  prefix 2: $s2"
Write-Output "  prefix 3: $s3"
Write-Output "  no prefix: $noPrefix"

$multiDef = ($lines | Where-Object { $_ -match "\*\*\*" }).Count
$withNikud = ($lines | Where-Object { $_ -match "\{" }).Count
Write-Output "  with *** (multiple defs): $multiDef"
Write-Output "  with {nikud}: $withNikud"

Write-Output "`nSample source=3 (first 8):"
$lines | Where-Object { $_ -match "^3 " } | Select-Object -First 8 | ForEach-Object { Write-Output "  $_" }

Write-Output "`nSample source=1 (first 8):"
$lines | Where-Object { $_ -match "^1 " } | Select-Object -First 8 | ForEach-Object { Write-Output "  $_" }

Write-Output "`nSample source=0 (first 8):"
$lines | Where-Object { $_ -match "^0 " } | Select-Object -First 8 | ForEach-Object { Write-Output "  $_" }
