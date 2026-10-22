<#
.SYNOPSIS
    התקנת-בדיקה מקומית של build CI של "כתבי הקודש" (תוסף VSTO ל-Word), כולל גיבוי ושחזור.

.DESCRIPTION
    סקריפט לבדיקה אישית בלבד — לא חלק מתהליך ההתקנה הרשמי של הפרויקט.
    מעתיק את תוכן תיקיית ה-build (KleiKodeshVsto_bin_Release, לצד הסקריפט הזה או
    בנתיב שסופק ב--BuildOutputPath) אל %LOCALAPPDATA%\KleiKodesh — בדיוק הנתיב והמבנה
    שבהם משתמש המתקין הרשמי (ראו Build/Installer/Helpers/AddinInstaller.cs:
    InstallPath = %LOCALAPPDATA%\KleiKodesh, אותו מבנה תיקיות שטוח).

    לפני כל שינוי יוצר גיבוי מלא של ההתקנה הקיימת תחת
    %LOCALAPPDATA%\KleiKodesh.backup-<timestamp>, כדי שאפשר יהיה לחזור אחורה
    בלי לאבד את ההתקנה הרשמית.

    הערה חשובה: ה-build הזה הגיע מ-CI (windows-latest) שבו שלב החתימה/יצירת
    ה-ClickOnce manifest (KleiKodesh.vsto) נכשל מסיבות תשתיתיות של ה-runner
    (ראו README-TEST.md) — ולכן אין בתיקיית ה-build קובץ KleiKodesh.vsto/.manifest.
    הסקריפט בכוונה לא נוגע בקבצי .vsto/.manifest הקיימים בהתקנה שלך (אם יש) —
    הוא רק מחליף את ה-DLL/EXE-ים עם הגרסה שנבדקת, ומשאיר את מנגנון האמון/המניפסט
    הקיים כפי שהוא. המשמעות: Word ימשיך לטעון את התוסף דרך אותו מניפסט ישן אם
    כבר יש התקנה תקינה; אם ה-DLL-ים השתנו באופן שה-manifest עדיין "חושב" שהם
    זהים (vstolocal הוא מצב אמון מקומי, לא אימות hash קשיח כמו ClickOnce רגיל),
    זה עובד להרבה מקרים בפועל — אבל אם Word מסרב לטעון את התוסף אחרי ההחלפה,
    זה הסימן לכך, ואז יש להריץ שחזור (ראה למטה) ולחכות ל-installer רשמי.

.PARAMETER BuildOutputPath
    נתיב לתיקיית ה-build (ברירת מחדל: "KleiKodeshVsto_bin_Release" לצד הסקריפט).

.PARAMETER InstallPath
    נתיב ההתקנה (ברירת מחדל: %LOCALAPPDATA%\KleiKodesh — זהה למתקין הרשמי).

.PARAMETER Restore
    במקום להתקין — משחזר את הגיבוי האחרון שנוצר, ומבטל את ההתקנה-לבדיקה.

.EXAMPLE
    .\install-test-build.ps1
    .\install-test-build.ps1 -Restore
#>

[CmdletBinding()]
param(
    [string]$BuildOutputPath = (Join-Path $PSScriptRoot "KleiKodeshVsto_bin_Release"),
    [string]$InstallPath     = (Join-Path $env:LOCALAPPDATA "KleiKodesh"),
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

# אותם ערכים בדיוק כמו Build/Installer/Helpers/AddinInstaller.cs — לא להמציא.
$AppName                = "KleiKodesh"
$AppDisplayName         = "כלי קודש"
$VstoFileName           = "KleiKodesh.vsto"
$AddinRegistryPath      = "Software\Microsoft\Office\Word\Addins\$AppName"
$AddinDataRegistryPath  = "Software\Microsoft\Office\Word\AddinsData\$AppName"

function Write-Section($text) {
    Write-Host ""
    Write-Host "== $text ==" -ForegroundColor Cyan
}

function Stop-KitveiHakodeshProcesses {
    foreach ($procName in @("KitveiHakodeshService", "כתבי הקודש")) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Host "  עוצר תהליך רץ: $procName"
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 800
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────
# -Restore: למצוא את הגיבוי האחרון ולהחזיר אותו במקום ההתקנה הנוכחית.
# ──────────────────────────────────────────────────────────────────────────
if ($Restore) {
    Write-Section "שחזור גיבוי"

    $backups = Get-ChildItem -Path (Split-Path $InstallPath -Parent) `
                              -Directory -Filter "$AppName.backup-*" -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending

    if (-not $backups -or $backups.Count -eq 0) {
        Write-Host "לא נמצא אף גיבוי (תיקיות בשם $AppName.backup-*) ליד $InstallPath." -ForegroundColor Yellow
        Write-Host "אין מה לשחזר — אולי מעולם לא הותקן build לבדיקה, או שהגיבוי נמחק ידנית." -ForegroundColor Yellow
        exit 1
    }

    $latestBackup = $backups[0].FullName
    Write-Host "נמצא גיבוי: $latestBackup"

    Stop-KitveiHakodeshProcesses

    if (Test-Path $InstallPath) {
        $failedDir = "$InstallPath.test-build-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Host "מעביר את ההתקנה-לבדיקה הנוכחית הצידה אל: $failedDir"
        Move-Item -Path $InstallPath -Destination $failedDir -Force
    }

    Write-Host "משחזר $latestBackup -> $InstallPath"
    Move-Item -Path $latestBackup -Destination $InstallPath -Force

    Write-Host ""
    Write-Host "שוחזרה ההתקנה הרשמית הקודמת." -ForegroundColor Green
    Write-Host "חשוב: סגור את Word לגמרי ופתח אותו מחדש כדי שהשינוי ייכנס לתוקף." -ForegroundColor Yellow
    exit 0
}

# ──────────────────────────────────────────────────────────────────────────
# התקנה
# ──────────────────────────────────────────────────────────────────────────
Write-Section "התקנת-בדיקה של כתבי הקודש (build מ-CI)"

if (-not (Test-Path $BuildOutputPath)) {
    Write-Host "תיקיית ה-build לא נמצאה: $BuildOutputPath" -ForegroundColor Red
    Write-Host "ודא שהסקריפט נמצא באותה תיקייה שחולצה מה-zip של ה-artifact (ליד KleiKodeshVsto_bin_Release)." -ForegroundColor Red
    exit 1
}

# שלב 1: גיבוי ההתקנה הקיימת, אם יש.
Write-Section "שלב 1/4 — גיבוי"
if (Test-Path $InstallPath) {
    $timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = "$InstallPath.backup-$timestamp"
    Write-Host "מגבה $InstallPath -> $backupPath"
    robocopy $InstallPath $backupPath /E /COPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Write-Host "robocopy נכשל בגיבוי (קוד $LASTEXITCODE) — עוצר לפני שמשנים משהו." -ForegroundColor Red
        exit 1
    }
    Write-Host "גיבוי הושלם." -ForegroundColor Green
} else {
    Write-Host "לא נמצאה התקנה קיימת תחת $InstallPath — מדלג על גיבוי (זו כנראה התקנה ראשונה)." -ForegroundColor Yellow
}

# שלב 2: עצירת תהליכים רצים כדי לא להעתיק מעל קובץ נעול.
Write-Section "שלב 2/4 — עצירת תהליכים רצים"
Stop-KitveiHakodeshProcesses

# שלב 3: העתקת קבצי ה-build, תוך שימור קבצי מניפסט/אמון קיימים ונתוני משתמש.
Write-Section "שלב 3/4 — העתקת קבצי ה-build"
New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null

# לא לדרוס: מניפסט ה-ClickOnce הקיים (ה-build הזה הגיע מ-CI בלי .vsto/.manifest —
# ראו README-TEST.md), ונתוני משתמש/מטמון/אינדקס שהמתקין הרשמי עצמו לעולם לא דורס
# (Build/Installer/Helpers/AddinInstaller.cs, ShouldSkipOnUpdate).
$excludeFiles = @("*.vsto", "*.manifest", "WebSitesWhitelist.json", "excluded_folders.json")
$excludeDirs  = @("BloomFilters", "cache", "webcache", "FtsIndex", "CatalogTocIndex",
                  "filesystemindex", "hb-cache", "convert-cache")

$robocopyArgs = @($BuildOutputPath, $InstallPath, "/E", "/COPY:DAT", "/R:2", "/W:1",
                   "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS", "/NP",
                   "/XF") + $excludeFiles + @("/XD") + $excludeDirs

robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Host "robocopy נכשל בהעתקת ה-build (קוד $LASTEXITCODE)." -ForegroundColor Red
    Write-Host "ההתקנה הקודמת עדיין בגיבוי תחת $backupPath — אפשר להריץ -Restore." -ForegroundColor Yellow
    exit 1
}
Write-Host "קבצי ה-build הועתקו אל $InstallPath" -ForegroundColor Green

if (-not (Get-ChildItem -Path $InstallPath -Filter "*.vsto" -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "שים לב: אין קובץ KleiKodesh.vsto בהתקנה (לא בגיבוי ולא ב-build החדש)." -ForegroundColor Yellow
    Write-Host "ל-Word אין איך לטעון תוסף VSTO בלי מניפסט כזה — ראה README-TEST.md." -ForegroundColor Yellow
}

# שלב 4: רישום/אימות מפתחות הרג'יסטרי של תוסף ה-VSTO (זהה בדיוק למתקין הרשמי).
Write-Section "שלב 4/4 — רישום ברג'יסטרי"

function Set-AddinRegistryKey($path) {
    $full = "HKCU:\$path"
    if (-not (Test-Path $full)) { New-Item -Path $full -Force | Out-Null }
    Set-ItemProperty -Path $full -Name "Description"  -Value $AppDisplayName
    Set-ItemProperty -Path $full -Name "FriendlyName" -Value $AppDisplayName
    Set-ItemProperty -Path $full -Name "Manifest"     -Value "file:///$InstallPath\$VstoFileName|vstolocal"
    New-ItemProperty -Path $full -Name "LoadBehavior" -Value 3 -PropertyType DWord -Force | Out-Null
}

Set-AddinRegistryKey $AddinRegistryPath
Set-AddinRegistryKey $AddinDataRegistryPath
Write-Host "מפתחות הרג'יסטרי עודכנו (HKCU\$AddinRegistryPath)" -ForegroundColor Green

Write-Section "סיום"
Write-Host "ההתקנה-לבדיקה הושלמה." -ForegroundColor Green
Write-Host ""
Write-Host "חשוב מאוד: סגור את Word לגמרי (מכל החלונות) ופתח אותו מחדש כדי שהשינוי ייכנס לתוקף." -ForegroundColor Yellow
Write-Host "כדי לחזור להתקנה הקודמת בכל שלב: .\install-test-build.ps1 -Restore" -ForegroundColor Yellow
