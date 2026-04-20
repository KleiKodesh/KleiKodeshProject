# File Encoding — Hebrew Source Files

## Current State

All source files in this project are now **UTF-8 without BOM**. Keep them that way.

They were originally Windows-1255 (Hebrew Windows encoding) and were converted to UTF-8 during the DocDesign rename in April 2026. Do not re-introduce Windows-1255.

---

## The Core Rule

**Never use PowerShell `Get-Content` / `Set-Content` on any source file in this project.**

PowerShell's default encoding handling silently corrupts Hebrew characters. The build still succeeds — the corruption only shows up at runtime as garbled `????` or `◆◆◆◆` text in the ribbon, task panes, and UI.

---

## What To Use Instead

### For single-file edits: use Kiro tools
`strReplace`, `fsWrite`, `readFile`, `semanticRename` — these handle encoding correctly. Use them for all single-file changes.

### For bulk text replacement across many files: use .NET APIs
```powershell
$encUtf8    = New-Object System.Text.UTF8Encoding($false)   # UTF-8, no BOM
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)  # strict validation

function SafeReplace($path, [hashtable]$replacements) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    # Validate it's UTF-8 (all files should be now)
    try { $null = $utf8Strict.GetString($bytes) }
    catch { throw "File is not valid UTF-8: $path — fix encoding before editing" }
    $text = [System.IO.File]::ReadAllText($path, $encUtf8)
    foreach ($key in $replacements.Keys) { $text = $text.Replace($key, $replacements[$key]) }
    [System.IO.File]::WriteAllText($path, $text, $encUtf8)
}
```

### For file rename operations: use `Rename-Item` only
`Rename-Item` moves bytes without touching content — safe. Never pipe the content through PowerShell strings during a rename.

---

## Verification — Run After Any Bulk Operation

```powershell
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$broken = @()
Get-ChildItem "." -Recurse -Include "*.cs","*.xaml","*.xml","*.csproj" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|packages|node_modules|\.git)\\' -and $_.Name -notmatch '\.g\.' } |
    ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        $hasNonAscii = ($bytes | Where-Object { $_ -gt 127 } | Measure-Object).Count -gt 0
        if (-not $hasNonAscii) { return }
        try { $null = $utf8Strict.GetString($bytes) }
        catch { $broken += $_.FullName }
    }
if ($broken) { $broken | ForEach-Object { Write-Host "BROKEN: $_" } }
else { Write-Host "All files valid UTF-8." }
```

Note: this scan is slow on the full repo due to `node_modules` and WebView2 cache. Scope it to specific project directories when possible:
```powershell
$dirs = @("DocDesign","KleiKodeshVsto/Ribbon","KleiKodeshVsto/Helpers","KleiKodeshVstoInstallerWpf/Pages")
```

---

## Recovery — If Corruption Happens Again

The canonical source of truth is **git**. If a file gets corrupted:

```powershell
# Restore from last commit
git checkout HEAD -- path/to/file.xaml

# Then reapply only the needed changes using SafeReplace above
```

For files not tracked by git (new files created during a session), the corruption chain is:
- Original UTF-8 → read as Windows-1255 → written as UTF-8 = **Mojibake** (`׳` pattern)
- Mojibake UTF-8 → read as Windows-1255 again → written as UTF-8 = **double Mojibake** (`×³â€º` pattern)

The only reliable fix for double-Mojibake is restoring from git. There is no safe automated reversal.

---

## What NOT to Do (Patterns That Caused This)

```powershell
# BAD — corrupts Hebrew
(Get-Content "file.cs" -Raw) -replace 'Old', 'New' | Set-Content "file.cs"

# BAD — corrupts Hebrew  
$content = Get-Content "file.xaml"
$content -replace 'Old', 'New' | Set-Content "file.xaml"

# BAD — corrupts Hebrew even with -Encoding UTF8 if the file has Hebrew
Get-Content "file.cs" -Raw -Encoding UTF8 | Set-Content "file.cs" -Encoding UTF8

# SAFE — use .NET APIs
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))

# SAFE — use Kiro strReplace tool for single files
```

---

## Files With Hebrew Content (Reference)

These files contain Hebrew strings and must stay UTF-8:

| File | Hebrew content |
|------|---------------|
| `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.xml` | Button labels, tooltips |
| `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.cs` | Task pane titles, error messages |
| `DocDesign/UI/DocDesignView.xaml` | All UI labels |
| `DocDesign/UI/DocDesignDictionary.xaml` | Style definitions |
| `DocDesign/Paragraphs/*.cs` | Hebrew comments |
| `DocDesign/Helpers/*.cs` | Hebrew comments |
| `KleiKodeshVstoInstallerWpf/Pages/*.xaml` | All installer UI |
| `KleiKodeshVstoInstallerWpf/Dialogs/*.xaml` | Dialog UI |
| `KleiKodeshVsto/KleiKodeshVsto.csproj` | Hebrew comments |
