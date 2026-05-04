[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$dll      = "C:\Users\Public\Documents\KleiKodeshProject\Kezayit\CSharpBackend\FtsLib\FtsLibTest\bin\Release\FtsLib.dll"
$sqliteDll = "C:\Users\Public\Documents\KleiKodeshProject\Kezayit\CSharpBackend\FtsLib\FtsLibTest\bin\Release\System.Data.SQLite.dll"
Add-Type -Path $sqliteDll
Add-Type -Path $dll

$indexDir = "C:\Users\Public\Documents\KleiKodeshProject\Kezayit\CSharpBackend\FtsLib\FtsLibTest\bin\Release\index_test_1M"
$reader   = New-Object FtsLib.Core.IndexReader($indexDir)

$t1 = [string]"כי"
$t2 = [string]"ביצחק"

$c1 = $reader.GetTermCount($t1)
$c2 = $reader.GetTermCount($t2)
Write-Output "Term '$t1' count: $c1"
Write-Output "Term '$t2' count: $c2"

$terms   = [string[]]@($t1, $t2)
$results = [System.Linq.Enumerable]::ToList($reader.Search([System.Collections.Generic.IEnumerable[string]]$terms))
Write-Output "AND result count: $($results.Count)"

$reader.Dispose()
