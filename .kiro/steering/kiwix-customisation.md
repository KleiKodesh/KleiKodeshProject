---
inclusion: fileMatch
fileMatchPattern: "KleiKodeshVsto/Kiwix/**"
---

# Kiwix JS Customisation

## Source of Truth

All Kiwix UI changes go in the **source**: `KleiKodeshVsto/Kiwix/kiwix-js-main/`

Never edit files in `KleiKodeshVsto/Kiwix/KiwixLib/Kiwix.js/` directly — they are build output and will be overwritten.

## Build and Deploy

```powershell
# 1. Build
Set-Location KleiKodeshVsto/Kiwix/kiwix-js-main
npm run build-src

# 2. Copy dist to KiwixLib
Set-Location ..
$src = Resolve-Path "kiwix-js-main/dist"
$dst = Resolve-Path "KiwixLib/Kiwix.js"
Get-ChildItem $src -Recurse -File | ForEach-Object {
    $rel  = $_.FullName.Substring($src.Path.Length + 1)
    $dest = Join-Path $dst $rel
    $dir  = Split-Path $dest
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item $_.FullName $dest -Force
}
```

## After npm install — Restore RTL Bootstrap

`npm install` restores the LTR Bootstrap 4. Always re-run this after any `npm install`:

```powershell
$wc = New-Object System.Net.WebClient
$wc.DownloadFile(
    "https://cdn.rtlcss.com/bootstrap/v4.5.3/css/bootstrap.min.css",
    (Resolve-Path "kiwix-js-main/node_modules/bootstrap/dist/css/bootstrap.min.css").Path
)
```

## Key Source Files

| File | Purpose |
|------|---------|
| `kiwix-js-main/i18n/he.jsonp.js` | Hebrew UI translation |
| `kiwix-js-main/www/js/init.js` | App config — `overrideBrowserLanguage` hardcoded to `'he'` (never reads localStorage) |
| `kiwix-js-main/www/js/app.js` | `he` added to supported-languages regex |
| `kiwix-js-main/www/index.html` | `dir="rtl" lang="he"` on `<html>`; language selector hidden, only Hebrew option kept |
| `kiwix-js-main/node_modules/bootstrap/dist/css/bootstrap.min.css` | Replaced with Bootstrap 4 RTL build |

## Change Log

Full history: `KleiKodeshVsto/Kiwix/KIWIX_CHANGES.md`

## C# Host (KiwixWebview.cs)

- `Load` event starts `InitAsync()` — not `HandleCreated` (fires too early)
- No `AddScriptToExecuteOnDocumentCreatedAsync` needed — Hebrew default is in `init.js`
- `try/catch` in `InitAsync` shows `MessageBox` on failure so splash never gets stuck
