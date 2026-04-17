# build-menu.ps1 - interactive top-level menu. All logic lives in build-installer.ps1.
. "$PSScriptRoot\build-helpers.ps1"

$installer = "$PSScriptRoot\build-installer.ps1"

function Show-Header([string]$title) {
    cls
    Write-Host ""
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""
}

function Read-Choice([string]$Prompt, [string[]]$Valid) {
    do {
        $v = Read-Host $Prompt
        if ($Valid -contains $v) { return $v }
        Write-Host "Please enter one of: $($Valid -join ', ')" -ForegroundColor Red
    } while ($true)
}

function Read-VersionArgs {
    # Returns a hashtable: @{ VersionIncrement=... } or @{ ManualVersion=... }
    Write-Host "Select version type:" -ForegroundColor Yellow
    Write-Host "  1. Patch  (bug fixes)        e.g. 1.2.3 -> 1.2.4"
    Write-Host "  2. Minor  (new features)     e.g. 1.2.3 -> 1.3.0"
    Write-Host "  3. Major  (breaking changes) e.g. 1.2.3 -> 2.0.0"
    Write-Host "  4. Manual (exact version)    e.g. v3.5.0"
    Write-Host ""
    $c = Read-Choice "Choice (1-4)" @("1","2","3","4")
    switch ($c) {
        "1" { return @{ VersionIncrement = "patch" } }
        "2" { return @{ VersionIncrement = "minor" } }
        "3" { return @{ VersionIncrement = "major" } }
        "4" {
            do {
                $v = (Read-Host "Enter version (e.g. v3.5.0)").Trim()
                if ($v -notmatch '^v') { $v = "v$v" }
                if ($v -match '^v\d+\.\d+\.\d+$') { return @{ ManualVersion = $v } }
                Write-Host "Invalid format. Use vX.Y.Z" -ForegroundColor Red
            } while ($true)
        }
    }
}

function Read-NotesSource {
    Write-Host ""
    Write-Host "Release notes source:" -ForegroundColor Yellow
    Write-Host "  1. Git commits only"
    Write-Host "  2. RELEASE_NOTES.txt only"
    Write-Host "  3. Both"
    Write-Host ""
    $c = Read-Choice "Choice (1-3)" @("1","2","3")
    return @("commits","file","both")[$c - 1]
}

function Confirm-Action([string]$summary) {
    Write-Host $summary
    Write-Host ""
    $c = Read-Host "Continue? (Y/N)"
    return $c -match '^[Yy]'
}

function Invoke-Installer([hashtable]$params) {
    # Splat the hashtable as named params to build-installer.ps1
    & powershell -NoLogo -ExecutionPolicy Bypass -File $installer @params
    Write-Host ""
    Read-Host "Press Enter to return to menu"
}

# -- Menu loop ----------------------------------------------------------------
while ($true) {
    Show-Header "KleiKodesh Build Menu"
    Write-Host "  1. Full Release Build + GitHub Release"
    Write-Host "  2. Release Build Only  (no GitHub release)"
    Write-Host "  3. Quick Build Test    (no version change)"
    Write-Host "  4. Clean Build         (wipe dirs first)"
    Write-Host "  5. View Build Info"
    Write-Host "  6. Open Build Folder"
    Write-Host "  7. Clear All GitHub Releases"
    Write-Host "  0. Exit"
    Write-Host ""
    $choice = Read-Choice "Choice (0-7)" @("0","1","2","3","4","5","6","7")

    switch ($choice) {

        "1" {
            Show-Header "Full Release Build + GitHub Release"
            $verArgs  = Read-VersionArgs
            $notes    = Read-NotesSource
            $summary  = if ($verArgs.ManualVersion) { "  Version : $($verArgs.ManualVersion) (manual)" } else { "  Version : increment $($verArgs.VersionIncrement)" }
            $summary += "`n  Notes   : $notes`n  Release : yes"
            if (Confirm-Action $summary) {
                Invoke-Installer ($verArgs + @{ ReleaseNotesSource = $notes })
            }
        }

        "2" {
            Show-Header "Release Build Only - No GitHub Release"
            $verArgs  = Read-VersionArgs
            $summary  = if ($verArgs.ManualVersion) { "  Version : $($verArgs.ManualVersion) (manual)" } else { "  Version : increment $($verArgs.VersionIncrement)" }
            $summary += "`n  Release : skipped"
            if (Confirm-Action $summary) {
                Invoke-Installer ($verArgs + @{ NoRelease = $true })
            }
        }

        "3" {
            Show-Header "Quick Build Test"
            $cur = Get-CurrentVersion
            Write-Host "Current version: $cur (will not be changed)" -ForegroundColor Cyan
            Write-Host ""
            if (Confirm-Action "  Build without version change, skip GitHub release.") {
                Invoke-Installer @{ ManualVersion = $cur; NoRelease = $true; NoClean = $true }
            }
        }

        "4" {
            Show-Header "Clean Build"
            Write-Host "Deletes existing installers and build dirs, then does a full release build." -ForegroundColor Yellow
            Write-Host ""
            if (-not (Confirm-Action "")) { break }

            Write-Host "Wiping old installers..." -ForegroundColor Yellow
            Remove-Item -Path (Join-Path $ReleasesDir "KleiKodeshSetup-*.exe") -ErrorAction SilentlyContinue
            Write-Host "Wiping build dirs..." -ForegroundColor Yellow
            @("KleiKodeshVsto\bin","KleiKodeshVsto\obj",
              "KleiKodeshVstoInstallerWpf\bin","KleiKodeshVstoInstallerWpf\obj") |
                ForEach-Object {
                    $p = Join-Path $ProjectRoot $_
                    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
                }

            $verArgs = Read-VersionArgs
            $notes   = Read-NotesSource
            Invoke-Installer ($verArgs + @{ ReleaseNotesSource = $notes; NoClean = $true })
        }

        "5" {
            Show-Header "Build Information"
            Write-Host "Installer files:" -ForegroundColor Yellow
            $files = Get-ChildItem -Path $ReleasesDir -Filter "KleiKodeshSetup-*.exe" -ErrorAction SilentlyContinue
            if ($files) { $files | ForEach-Object { Write-Host "  $($_.Name)" } }
            else         { Write-Host "  (none)" -ForegroundColor Gray }
            Write-Host ""
            try { Write-Host "Current source version: $(Get-CurrentVersion)" -ForegroundColor Cyan }
            catch { Write-Host "Could not read version." -ForegroundColor Red }
            Write-Host ""
            Read-Host "Press Enter to return"
        }

        "6" { explorer $BuildDir }

        "7" {
            Show-Header "Clear All GitHub Releases"
            Write-Host "WARNING: permanently deletes ALL GitHub releases and tags!" -ForegroundColor Red
            Write-Host ""
            if (-not (Confirm-Action "")) { break }
            $confirm2 = Read-Host "Type DELETE to confirm"
            if ($confirm2 -ne "DELETE") { Write-Host "Cancelled."; Start-Sleep 1; break }

            $releases = gh release list --limit 1000 --json tagName | ConvertFrom-Json
            if ($releases.Count -eq 0) {
                Write-Host "No releases found." -ForegroundColor Gray
            } else {
                Write-Host "Deleting $($releases.Count) release(s)..." -ForegroundColor Yellow
                foreach ($r in $releases) {
                    Write-Host "  $($r.tagName)" -ForegroundColor Gray
                    gh release delete $r.tagName --yes --cleanup-tag
                }
                Write-Host "Done." -ForegroundColor Green
            }
            Write-Host ""
            Read-Host "Press Enter to return"
        }

        "0" { exit 0 }
    }
}
