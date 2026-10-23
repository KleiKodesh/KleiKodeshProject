<#
  apply-update.ps1  -  "עדכון ריבוי ספריות" (גרסת תצוגה, לא רשמית)

  מעדכן התקנה קיימת של "כתבי הקודש" הרשמי: מחליף רק את
    KitveiHakodeshLib.dll
    KitveiHakodeshService\   (הקבצים שהחבילה נושאת: KitveiHakodeshService.exe, e_sqlite3.dll ...)
    KitveiHakodesh\          (ממשק המשתמש)
  ולא נוגע בשום דבר אחר: לא FluentChromeTabs.dll, לא KleiKodesh.vsto/.manifest, לא בקבצי נתונים,
  מטמון ואינדקסים, ואינו רושם מחדש את התוסף (אין כתיבה ל-registry).

  פרמטרים:
    -PayloadDir  תיקיית החבילה (מכילה payload-manifest.json + הקבצים)
    -TargetDir   תיקיית ההתקנה הרשמית (ברירת מחדל: %LOCALAPPDATA%\KleiKodesh)
    -TestMode    מצב בדיקה: לא נוגע בשירות ולא ב-registry (נבחר אוטומטית כשנמסר /DIR=)
    -NoRestart   לא להפעיל את השירות בסיום

  קודי יציאה: 0 הצלחה | 2 ההתקנה הרשמית לא נמצאה | 3 לא ניתן היה לעצור את השירות
              4 החבילה פגומה | 5 העתקה נכשלה (בוצע שחזור אוטומטי)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [string]$TargetDir,
    [switch]$TestMode,
    [switch]$NoRestart,
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'
# מנקה נתיבי מודולים של PowerShell אחר שעלולים להיות בסביבה שהורשה (למשל pwsh 7), אחרת cmdlets כמו Get-Service לא נטענים
$env:PSModulePath = (Join-Path $PSHOME 'Modules')
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KleiKodesh'
}
$TargetDir = [IO.Path]::GetFullPath($TargetDir).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path ([IO.Path]::GetTempPath()) 'KleiKodesh-multilib-update.log'
}

$ServiceName = 'KitveiHakodeshSvc'
$PipeName    = 'KitveiHakodesh'
$ServiceDir  = 'KitveiHakodeshService'
$ServiceExe  = 'KitveiHakodeshService.exe'

function Log([string]$m) {
    $line = '[{0:HH:mm:ss}] {1}' -f (Get-Date), $m
    try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch { }
    Write-Host $m
}

# קבצים/תיקיות שהעדכון אסור לו לגעת בהם, גם אם בטעות הופיעו בחבילה (רשימת הגנה).
function Test-Protected([string]$rel) {
    $n = $rel.Replace('/', '\')
    if ($n -match '(^|\\)FluentChromeTabs[^\\]*$')             { return $true }
    if ($n -match '\.(vsto|manifest)$')                         { return $true }
    if ($n -match '(^|\\)KleiKodesh[^\\]*\.dll(\.config)?$')    { return $true }
    if ($n -match '^KitveiHakodesh\\(webcache|cache)[^\\]*(\\|$)') { return $true }
    if ($n -match '^KitveiHakodeshService\\(FtsIndex|CatalogTocIndex|filesystemindex|hb-cache|convert-cache|Dictionary|HebrewBooks|SearchExpansion|updates)(\\|$)') { return $true }
    if ($n -ieq 'KitveiHakodeshService\excluded_folders.json')  { return $true }
    if ($n -match '^(BloomFilters|filesystemindex|Resources|runtimes|Kiwix\.js)(\\|$)') { return $true }
    return $false
}

# קבצי הגדרות: נוצרים רק אם חסרים, ולעולם לא דורסים קובץ קיים (ייתכן שהמשתמש שינה אותו).
function Test-KeepIfExists([string]$rel) {
    return ($rel.Replace('/', '\') -ieq 'KitveiHakodeshService\appsettings.json')
}

function Get-Sha([string]$path) {
    $s = [IO.File]::OpenRead($path)
    try { $h = [Security.Cryptography.SHA256]::Create(); return ([BitConverter]::ToString($h.ComputeHash($s))).Replace('-', '') }
    finally { $s.Dispose() }
}

# ---- שליחת פעולה לצינור השירות (כמו המתקין הרשמי: מסגרת MessagePack {Op:name}) ----
function Test-PipeListening {
    try { return [bool]([IO.Directory]::EnumerateFiles('\\.\pipe\') | Where-Object { [IO.Path]::GetFileName($_) -ieq $PipeName } | Select-Object -First 1) }
    catch { return $false }
}
function Send-ServiceOp([string]$op) {
    try {
        $opBytes = [Text.Encoding]::UTF8.GetBytes($op)
        $frame = New-Object byte[] (5 + $opBytes.Length)
        $frame[0] = 0x81; $frame[1] = 0xA2; $frame[2] = [byte][char]'O'; $frame[3] = [byte][char]'p'
        $frame[4] = [byte](0xA0 -bor $opBytes.Length)
        [Array]::Copy($opBytes, 0, $frame, 5, $opBytes.Length)
        $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $PipeName, [IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(2000)
            $len = [BitConverter]::GetBytes([int]$frame.Length)
            $pipe.Write($len, 0, 4); $pipe.Write($frame, 0, $frame.Length); $pipe.Flush()
            $buf = New-Object byte[] 4
            [void]$pipe.Read($buf, 0, 4)   # תשובה או EOF
        } finally { $pipe.Dispose() }
    } catch { }
}
function Test-ServiceRegistered {
    return (Test-Path -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName")
}
function Get-ServiceState {
    try { return (Get-Service -Name $ServiceName -ErrorAction Stop).Status.ToString() } catch { return $null }
}
function Stop-HostedService {
    # 1. בקשת shutdown דרך הצינור (ללא הרשאות מנהל)  2. המתנה  3. סגירת עותק קונסולה שרץ מתיקיית ההתקנה
    Send-ServiceOp 'shutdown'
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $st = Get-ServiceState
        if (-not (Test-PipeListening) -and ($null -eq $st -or $st -eq 'Stopped')) { break }
        Start-Sleep -Milliseconds 300
    }
    $svcFolder = Join-Path $TargetDir $ServiceDir
    foreach ($p in @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ServiceExe)) -ErrorAction SilentlyContinue)) {
        try {
            $path = $p.MainModule.FileName
            if ($path -and $path.StartsWith($svcFolder, [StringComparison]::OrdinalIgnoreCase)) {
                $p.Kill(); [void]$p.WaitForExit(5000)
            }
        } catch { }
    }
}

# כל חריגה לא צפויה נרשמת ביומן ומחזירה קוד 6
trap { Log ('שגיאה לא צפויה: ' + ($_ | Out-String)); exit 6 }

# ---- 1. אימות ההתקנה הרשמית ----
$msgNotFound = 'לא נמצאה התקנה של "כתבי הקודש" הרשמי. התקן קודם את כתבי הקודש הרשמי, ואז הרץ שוב את העדכון.'
Log "תיקיית יעד: $TargetDir  (מצב בדיקה: $([bool]$TestMode))"
$hasAddin = (Test-Path -LiteralPath (Join-Path $TargetDir 'KleiKodesh.vsto')) -or (Test-Path -LiteralPath (Join-Path $TargetDir 'KleiKodesh.dll'))
$hasLib   = Test-Path -LiteralPath (Join-Path $TargetDir 'KitveiHakodeshLib.dll')
if (-not (Test-Path -LiteralPath $TargetDir) -or -not $hasAddin -or -not $hasLib) {
    Log $msgNotFound
    exit 2
}

# ---- 2. טעינת רשימת החבילה ואימות שלמותה ----
$manifestPath = Join-Path $PayloadDir 'payload-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { Log 'החבילה פגומה: payload-manifest.json חסר.'; exit 4 }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$items = @()
foreach ($f in $manifest.files) {
    $rel = [string]$f.path
    if (Test-Protected $rel) { Log "דילוג על נתיב מוגן שהופיע בחבילה: $rel"; continue }
    if ($rel -match '\.\.') { Log "נתיב לא חוקי בחבילה: $rel"; exit 4 }
    $src = Join-Path $PayloadDir $rel
    if (-not (Test-Path -LiteralPath $src)) { Log "החבילה פגומה: חסר $rel"; exit 4 }
    if ((Get-Item -LiteralPath $src).Length -ne [int64]$f.size -or (Get-Sha $src) -ne ([string]$f.sha256).ToUpperInvariant()) {
        Log "החבילה פגומה: סיכום הביקורת של $rel אינו תואם."; exit 4
    }
    $items += [pscustomobject]@{ Rel = $rel; Src = $src; Sha = ([string]$f.sha256).ToUpperInvariant(); Size = [int64]$f.size }
}
Log "קבצים בחבילה: $($items.Count)"

# ---- 3. מה באמת צריך להשתנות ----
$changed = @(); $created = @()
foreach ($it in $items) {
    $dst = Join-Path $TargetDir $it.Rel
    if (Test-Path -LiteralPath $dst -PathType Leaf) {
        if (Test-KeepIfExists $it.Rel) { continue }
        if ((Get-Sha $dst) -ne $it.Sha) { $changed += $it }
    } else {
        $created += $it
    }
}
$todo = @($changed) + @($created)
if ($todo.Count -eq 0) {
    Log 'ההתקנה כבר מעודכנת - אין מה להחליף. לא בוצע שינוי, לא נוצר גיבוי, השירות לא הופסק.'
    exit 0
}
Log "קבצים להחלפה: $($changed.Count) | קבצים חדשים: $($created.Count)"

# ---- 4. גיבוי ----
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = "$TargetDir.multilib-backup-$stamp"
$backupFiles = Join-Path $backupRoot 'files'
New-Item -ItemType Directory -Force -Path $backupFiles | Out-Null
foreach ($it in $changed) {
    $dst = Join-Path $TargetDir $it.Rel
    $bk  = Join-Path $backupFiles $it.Rel
    New-Item -ItemType Directory -Force -Path (Split-Path $bk -Parent) | Out-Null
    Copy-Item -LiteralPath $dst -Destination $bk -Force
}
[IO.File]::WriteAllLines((Join-Path $backupRoot 'new-files.txt'), [string[]]@($created | ForEach-Object { $_.Rel }), (New-Object Text.UTF8Encoding $true))
[IO.File]::WriteAllLines((Join-Path $backupRoot 'replaced-files.txt'), [string[]]@($changed | ForEach-Object { $_.Rel }), (New-Object Text.UTF8Encoding $true))

# סקריפט שחזור לצד הגיבוי
$restore = @'
# שחזור לגרסה הרשמית: מחזיר את הקבצים שהעדכון החליף ומסיר את הקבצים שהוא הוסיף.
param([string]$Target = '__TARGET__', [switch]$TestMode)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$pipe = 'KitveiHakodesh'
if (-not $TestMode) {
    try {
        $op = [Text.Encoding]::UTF8.GetBytes('shutdown')
        $frame = New-Object byte[] (5 + $op.Length)
        $frame[0]=0x81;$frame[1]=0xA2;$frame[2]=[byte][char]'O';$frame[3]=[byte][char]'p';$frame[4]=[byte](0xA0 -bor $op.Length)
        [Array]::Copy($op,0,$frame,5,$op.Length)
        $c = New-Object IO.Pipes.NamedPipeClientStream('.',$pipe,[IO.Pipes.PipeDirection]::InOut)
        $c.Connect(2000); $c.Write([BitConverter]::GetBytes([int]$frame.Length),0,4); $c.Write($frame,0,$frame.Length); $c.Flush(); $c.Dispose()
    } catch { }
    Start-Sleep -Seconds 4
}
$files = Join-Path $here 'files'
Get-ChildItem -LiteralPath $files -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($files.Length).TrimStart('\')
    $dst = Join-Path $Target $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
    Write-Host "שוחזר: $rel"
}
$newList = Join-Path $here 'new-files.txt'
if (Test-Path $newList) {
    Get-Content -LiteralPath $newList -Encoding UTF8 | Where-Object { $_.Trim() } | ForEach-Object {
        $p = Join-Path $Target $_
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; Write-Host "הוסר קובץ שנוסף: $_" }
    }
}
if (-not $TestMode) { try { & sc.exe start KitveiHakodeshSvc | Out-Null } catch { } }
Write-Host 'השחזור הסתיים.'
'@
$restore = $restore.Replace('__TARGET__', $TargetDir.Replace("'", "''"))
[IO.File]::WriteAllText((Join-Path $backupRoot 'restore.ps1'), $restore, (New-Object Text.UTF8Encoding $true))
Log "גיבוי נשמר ב: $backupRoot  (שחזור: restore.ps1 באותה תיקייה)"

# ---- 5. עצירת השירות (רק מחוץ למצב בדיקה) ----
$serviceWasUp = $false
if (-not $TestMode) {
    $serviceWasUp = (Test-PipeListening) -or ((Get-ServiceState) -eq 'Running')
    if ($serviceWasUp) {
        Log 'עוצר את שירות כתבי הקודש (shutdown דרך הצינור, כמו במתקין הרשמי)...'
        Stop-HostedService
        if ((Test-PipeListening) -or ((Get-ServiceState) -notin @($null, 'Stopped'))) {
            Log 'לא הצלחתי לעצור את השירות. הפעולה בוטלה, לא שונה דבר. סגור את Word ואת כתבי הקודש, ואם צריך עצור את השירות ידנית: פתח "שירותים" (services.msc), עצור את KitveiHakodeshSvc, והרץ שוב.'
            exit 3
        }
    }
} else {
    Log 'מצב בדיקה: אין נגיעה בשירות ובמרשם (registry).'
}

# ---- 6. העתקה (דרך קובץ זמני, עם ניסיונות חוזרים) ----
$done = New-Object System.Collections.Generic.List[object]
$failure = $null
foreach ($it in $todo) {
    $dst = Join-Path $TargetDir $it.Rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    $tmp = "$dst.mlp-new"
    $ok = $false
    for ($try = 1; $try -le 5 -and -not $ok; $try++) {
        try {
            Copy-Item -LiteralPath $it.Src -Destination $tmp -Force
            Move-Item -LiteralPath $tmp -Destination $dst -Force
            if ((Get-Sha $dst) -ne $it.Sha) { throw 'אימות לאחר העתקה נכשל' }
            $ok = $true
        } catch {
            $failure = "$($it.Rel): $($_.Exception.Message)"
            if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Milliseconds (500 * $try)
        }
    }
    if (-not $ok) { break }
    $failure = $null
    $done.Add($it)
}

if ($failure) {
    Log "ההעתקה נכשלה ב- $failure"
    Log 'מבצע שחזור אוטומטי של הקבצים שכבר הוחלפו...'
    foreach ($it in $done) {
        $dst = Join-Path $TargetDir $it.Rel
        $bk = Join-Path $backupFiles $it.Rel
        try {
            if (Test-Path -LiteralPath $bk) { Copy-Item -LiteralPath $bk -Destination $dst -Force }
            elseif (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force }
        } catch { Log "אזהרה: שחזור $($it.Rel) נכשל: $($_.Exception.Message)" }
    }
    Log 'ייתכן ש-Word פתוח ונועל את KitveiHakodeshLib.dll. סגור את Word והרץ שוב את העדכון.'
    if (-not $TestMode -and $serviceWasUp -and -not $NoRestart) { try { & sc.exe start $ServiceName | Out-Null } catch { } }
    exit 5
}

# ---- 7. הפעלת השירות ----
if (-not $TestMode -and -not $NoRestart -and (Test-ServiceRegistered)) {
    Log 'מפעיל את השירות...'
    try { & sc.exe start $ServiceName | Out-Null } catch { }
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not (Test-PipeListening)) { Start-Sleep -Milliseconds 300 }
    if (Test-PipeListening) { Log 'השירות פועל.' }
    else { Log 'השירות לא הופעל מיד (ייתכן שחסרה הרשאה). הוא יופעל אוטומטית בפתיחת Word / כתבי הקודש.' }
}

Log "העדכון הושלם: הוחלפו $($changed.Count) קבצים, נוספו $($created.Count). גיבוי: $backupRoot"
exit 0
