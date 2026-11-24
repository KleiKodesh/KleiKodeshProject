<#
.SYNOPSIS
    Turns release-notes.json (written by ai-release-plan.ps1) into the GitHub release text:
    Hebrew first (right-to-left block), then English.
.EXAMPLE
    .github/scripts/notes-to-markdown.ps1 -Json release-notes.json -Out release-notes.md
#>
param(
    [Parameter(Mandatory)] [string] $Json,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$notes = [IO.File]::ReadAllText((Resolve-Path $Json), $utf8) | ConvertFrom-Json

$headings = @{
    he = @{ new = 'חדש'; improved = 'שיפורים'; fixed = 'תיקונים' }
    en = @{ new = 'New'; improved = 'Improved'; fixed = 'Fixed' }
}
$autoLine = @{
    he = '*גרסה זו פורסמה אוטומטית במסגרת השחרור השבועי האוטומטי.*'
    en = '*This version was published automatically by the weekly automatic release.*'
}

# One language: a heading per section; one bullet per area, its changes as sub-points
# (a single change stays on the area's line).
function Format-Language([string] $lang) {
    $lines = foreach ($section in $notes.sections) {
        "### $($headings[$lang][$section.kind])"
        foreach ($area in $section.areas) {
            $changes = @($area.changes)
            if ($changes.Count -eq 1) { "- **$($area.$lang)** - $($changes[0].$lang)" }
            else {
                "- **$($area.$lang)**"
                foreach ($change in $changes) { "  - $($change.$lang)" }
            }
        }
        ''
    }
    if ($notes.auto) { $lines += $autoLine[$lang] }
    ($lines -join "`n").Trim()
}

$markdown = @"
<div dir="rtl">

$(Format-Language 'he')

</div>

---

$(Format-Language 'en')
"@
[IO.File]::WriteAllText($Out, $markdown, $utf8)
Write-Host $markdown
