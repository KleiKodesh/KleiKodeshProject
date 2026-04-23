# Kiwix JS — KleiKodesh Customisation Log

All modifications to the upstream [kiwix-js](https://github.com/kiwix/kiwix-js) source are documented here.
Source: `KleiKodeshVsto/Kiwix/kiwix-js-main/`
Built output: `KleiKodeshVsto/Kiwix/KiwixLib/Kiwix.js/`

---

## Workflow

1. Make changes in `kiwix-js-main/` source files.
2. Run `npm run build-src` from `kiwix-js-main/`.
3. Copy `dist/` output to `KiwixLib/Kiwix.js/`.
4. Document the change below.

```powershell
# From repo root — build and copy:
Set-Location KleiKodeshVsto/Kiwix/kiwix-js-main
npm run build-src
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

### After npm install — restore RTL Bootstrap

`npm install` restores the LTR Bootstrap. Re-run this after any `npm install`:

```powershell
$wc = New-Object System.Net.WebClient
$wc.DownloadFile(
    "https://cdn.rtlcss.com/bootstrap/v4.5.3/css/bootstrap.min.css",
    (Resolve-Path "kiwix-js-main/node_modules/bootstrap/dist/css/bootstrap.min.css").Path
)
```

---

## Changes

### 2026-04 — Hebrew UI + RTL Layout

**Source files changed:**

| File | Change |
|------|--------|
| `i18n/he.jsonp.js` | New file. Full Hebrew translation of all UI strings. |
| `www/js/init.js` | `overrideBrowserLanguage` hardcoded to `'he'` — never reads from localStorage. Ensures Hebrew is the default regardless of browser language. |
| `www/index.html` | Language selector restored with Hebrew (`עברית`) added as an option. User can switch languages via the dropdown in Configuration. |
| `www/js/app.js` | Added `he` to the supported-languages regex in `getDefaultLanguageAndTranslateApp()`. |
| `www/index.html` | Added `dir="rtl" lang="he"` to `<html>` tag. |
| `node_modules/bootstrap/dist/css/bootstrap.min.css` | Replaced with Bootstrap 4 RTL build v4.5.3 from cdn.rtlcss.com. All margins, paddings, floats and flex directions are mirrored. See note above about restoring after npm install. |
| `www/css/app.css` | Added RTL override block at end of file. Flips `border-left/right`, `padding-left/right`, `margin-left/right` for snippet indentation, TOC items, navbar, and form controls. |

---

## C# Host (KiwixWebview.cs)

| Change | Reason |
|--------|--------|
| `Load` event instead of `HandleCreated` for `InitAsync()` | `Load` fires after all Win32 handles exist. `HandleCreated` fired too early in the WinForms demo app. |
| Removed `AddScriptToExecuteOnDocumentCreatedAsync` localStorage injection | Hebrew default is now in `init.js` source. No runtime injection needed. |
| `try/catch` around `InitAsync` with `MessageBox` on failure | Prevents infinite splash screen when WebView2 init fails silently. |

---

## Demo App (KiwixDemoApp)

| Change | Reason |
|--------|--------|
| Converted from WPF to pure WinForms | `KiwixWebview` is a WinForms `UserControl`. WPF wrapper was pointless overhead and caused the splash-stuck bug. |
| `WebView2Loader.dll` as flat `Content` item in csproj | Native DLL does not propagate transitively through project references. Must be copied explicitly next to the exe. |
| WebView2 package version updated to `1.0.3912.50` | Only this version is installed in the packages folder. |
