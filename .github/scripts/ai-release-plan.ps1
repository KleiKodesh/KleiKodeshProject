# Asks Gemini to read the commits since the last release and decide
#   1. the version bump (major / minor / patch)
#   2. user-facing release notes, in English and Hebrew
# Writes the notes to RELEASE_NOTES.txt (build-installer.ps1 -ReleaseNotesSource file
# picks them up) and the bump to $env:GITHUB_OUTPUT as "bump=...".
#
# Env: GEMINI_API_KEY, GH_TOKEN. Optional: GEMINI_MODEL.
#
# -Tag vX.Y.Z: write notes for that existing release instead (commits since the
# release before it) - used by rewrite-release-notes.yml.
#
# -Auto: this is the weekly automatic release - say so at the end of both languages.
param([string]$OutFile = "RELEASE_NOTES.txt", [string]$Tag, [switch]$Auto,
      # -NotesOnly: write $OutFile only - no installer notes files, no bump output.
      # Used by release-notes-test.yml to try the prompt without touching a release.
      [switch]$NotesOnly,
      # -Since vX.Y.Z: override the previous tag (test runs).
      [string]$Since)

$ErrorActionPreference = 'Stop'
# Hebrew commit messages: read git's output as UTF-8 (Windows PowerShell 5.1 locally).
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
# Tried in order; each model has its own free-tier quota, so a maxed-out first one
# still leaves the second (Pro has a zero free-tier quota - don't use it).
# No AI notes = no release (the step fails, nothing is built).
$models = if ($env:GEMINI_MODEL) { @($env:GEMINI_MODEL -split ',' | ForEach-Object Trim) } else { @('gemini-flash-latest', 'gemini-flash-lite-latest') }

$tags = @(gh release list --limit 200 --exclude-drafts --json tagName --jq '.[].tagName')   # newest first
$previousTag = if ($Since) { $Since } elseif ($Tag) { $tags[[array]::IndexOf($tags, $Tag) + 1] } else { $tags[0] }
$end     = if ($Tag) { $Tag } else { 'HEAD' }
$range   = if ($previousTag) { "$previousTag..$end" } else { "-30" }

# One block per commit: subject, body, and the app areas its files touch. The paths
# are what tells Gemini a commit is internal (CI, docs, service plumbing) even when the
# subject sounds user-facing. Commits marked "User-facing: none" are dropped here so
# Gemini never sees them.
$hashes = @(git log $range --no-merges --pretty=format:%H)
$blocks = foreach ($h in $hashes) {
    $msg = (git log -1 --pretty=format:"%s%n%b" $h | Out-String).Trim()
    if ($msg -match '(?im)^\s*User-facing:\s*none\b') { continue }
    $files = @(git diff-tree --no-commit-id --name-only -r $h)
    # Nothing a user runs: CI, build scripts, docs, notes, tests.
    $shipped = @($files | Where-Object { $_ -notmatch '^(\.github|\.kiro|\.claude|Build/scripts|docs?)/|\.md$|^suggestions\.txt$|^todo\.txt$|(^|/)tests?/|\.(test|spec)\.[jt]s$' })
    if ($files -and -not $shipped) { continue }
    # Long bodies are implementation detail; the subject and first lines carry the gist.
    if ($msg.Length -gt 400) { $msg = $msg.Substring(0, 400) + '...' }
    $areas = @($shipped |
        ForEach-Object { $p = @($_ -split '/'); if ($p.Count -gt 1) { $p[0..([Math]::Min(4, $p.Count - 2))] -join '/' } else { $_ } } |
        Sort-Object -Unique | Select-Object -First 6)
    if ($env:NOTES_INPUT -eq 'plain') { "### $msg" } else { "### $msg`nFiles: $($areas -join ', ')" }
}
$commits = ($blocks -join "`n`n").Trim()
if (-not $commits) { throw "No commits since $previousTag - nothing to release." }
Write-Host "Previous release: $previousTag ($($hashes.Count) commits, $(@($blocks).Count) sent)"

$blocks = @($blocks)
# map (default): pass 1 judges commits in small batches so none are skipped, pass 2
# merges the per-commit lines into the notes. single: one prompt with all commits.
$mode = if ($env:NOTES_MODE) { $env:NOTES_MODE } else { 'single' }

$about = @"
You write the "What's new" notes for KleiKodesh, a free Windows app for Torah study:
the "Kitvei Hakodesh" library app (book view with commentaries, full-text search, book
catalog, HebrewBooks scans, PDF viewer, dictionary, calendar and daily learning) and the
"Klei Kodesh" Microsoft Word add-in (Torani Design, Dicta Nakdan, Regex Find, Websites,
Kiwix, installer settings, About page, macros). Readers are Torah scholars and yeshiva
students. They are not programmers and do not care how anything works inside - only what
they can now do, what got better, and what no longer goes wrong.
"@

$rules = @"
SECTIONS:
- "new" = something users could not do at all before: a new feature, option, button,
  keyboard shortcut, language, download, page.
- "improved" = something that already existed now works better, faster, clearer, or a
  bit differently (e.g. a Torani Design row that now changes letters in another way is
  improved, not new; a faster home page is improved).
- "fixed" = something that went wrong before (wrong text, lost position, scrambled copy,
  failed print, wrong direction) and now does not.

WORDING:
- Describe the effect a user sees, never the mechanism. Bad: "custom scrollbar",
  "native indexing", "filter pills", "virtualized rendering", "character scaling",
  "cache", "lazy load". Good: "the scrollbar shows which section you are in", "results
  appear almost at once", "filter definitions by source", "letter stretch widens the
  letters themselves".
- Never mention internal names: WindowedScroller, TanStack, Lucene, FTS, FtsLib, Core,
  service, LinesTable, localStorage, IndexedDB, MessagePack, Vue, VSTO, COM, ref, flush,
  cache, quota, schema, op, API, Fluent, pills, stream, chunk, index, native, render,
  "TOC" (say table of contents), "lines pane", hashes, file names, pixel/ms numbers.
- Word add-in changes (ribbon, panes, installer settings, About, macros, interface
  language) get area "Klei Kodesh (Word add-in)" or the exact ribbon tool name
  (e.g. "Torani Design"), never "Settings" (Settings is the library app's page).
- TERMINOLOGY: area names and button/feature names MUST use the exact glossary term.
  If something is not in the glossary, describe it in plain words, do not coin a name.

STYLE EXAMPLES (from older releases, same tone wanted):
- new | Calendar | Tap a date to see that day's zmanim and daily learning.
- improved | Book catalog | Opening a large category is instant and keeps your scroll place.
- fixed | Tabs | Closing a tab no longer jumps to the first tab.
- new | Dicta Nakdan | Vocalize only the selected words.
"@

$glossary = Get-Content (Join-Path $PSScriptRoot 'release-glossary.txt') -Raw -Encoding UTF8

$changeSchema = @{
    type       = 'OBJECT'
    properties = @{
        section = @{ type = 'STRING'; enum = @('new', 'improved', 'fixed') }
        area_en = @{ type = 'STRING' }
        area_he = @{ type = 'STRING' }
        en      = @{ type = 'STRING' }
        he      = @{ type = 'STRING' }
        rank    = @{ type = 'INTEGER' }
    }
    required   = @('section', 'area_en', 'area_he', 'en', 'he', 'rank')
    propertyOrdering = @('section', 'area_en', 'area_he', 'en', 'he', 'rank')
}
$planSchema = @{
    type       = 'OBJECT'
    properties = @{
        changes = @{ type = 'ARRAY'; items = $changeSchema }
        bump    = @{ type = 'STRING'; enum = @('major', 'minor', 'patch') }
        reason  = @{ type = 'STRING' }
    }
    required   = @('changes', 'bump', 'reason')
    propertyOrdering = @('changes', 'bump', 'reason')
}

# One generateContent call, trying each model in turn; returns the parsed JSON or $null.
function Invoke-Gemini([string]$text, $schema, [scriptblock]$valid) {
    $body = @{
        contents         = @(@{ parts = @(@{ text = $text }) })
        generationConfig = @{ temperature = 0.2; responseMimeType = 'application/json'; responseSchema = $schema }
    } | ConvertTo-Json -Depth 30
    foreach ($model in $models) {
        foreach ($attempt in 1..3) {
            try {
                $resp = Invoke-RestMethod -Method Post -TimeoutSec 180 `
                    -Uri "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent" `
                    -Headers @{ 'x-goog-api-key' = $env:GEMINI_API_KEY } `
                    -ContentType 'application/json; charset=utf-8' `
                    -Body ([Text.Encoding]::UTF8.GetBytes($body))
                $out = $resp.candidates[0].content.parts[0].text | ConvertFrom-Json
                if (& $valid $out) { Write-Host "  model: $model"; return $out }
                throw "incomplete answer"
            } catch {
                Write-Host "  $model attempt $attempt failed: $($_.Exception.Message)"
                if ($_.Exception.Message -match '404') { break }   # model not available - next one
                if ($attempt -lt 3) { Start-Sleep -Seconds (30 * $attempt) }
            }
        }
    }
    $null
}

$plan = $null
if ($mode -eq 'map') {
    $noteSchema = @{
        type       = 'OBJECT'
        properties = @{ notes = @{ type = 'ARRAY'; items = @{
            type       = 'OBJECT'
            properties = @{
                id      = @{ type = 'INTEGER' }
                visible = @{ type = 'BOOLEAN' }
                section = @{ type = 'STRING'; enum = @('new', 'improved', 'fixed', 'none') }
                area_en = @{ type = 'STRING' }
                en      = @{ type = 'STRING' }
            }
            required   = @('id', 'visible', 'section', 'area_en', 'en')
            propertyOrdering = @('id', 'visible', 'section', 'area_en', 'en')
        } } }
        required   = @('notes')
    }
    $size = 15
    $found = @()
    for ($i = 0; $i -lt $blocks.Count; $i += $size) {
        $batch = @($blocks[$i..([Math]::Min($i + $size, $blocks.Count) - 1)])
        $numbered = for ($k = 0; $k -lt $batch.Count; $k++) { "[id $($i + $k)]`n$($batch[$k])" }
        $p1 = @"
$about

Below are $($batch.Count) developer commit messages. For EACH commit return one entry
with the same id: is anything in it visible to a user of the app or the Word add-in?
- visible=false (section "none", empty texts) for: CI, GitHub Actions, build/release/
  signing scripts, docs, tests, refactors, renames, code removal, background service
  plumbing, logging, licence text, version bumps, pure internal clean-up.
- visible=true otherwise - including speed-ups a user would feel, small fixes, new
  options, English interface work, installer settings, About page, macros, downloads.
- A body line "User-facing: <section> - <Area>: <sentence>" is the author's own verdict:
  copy it. "User-facing: none" means visible=false.
- area_en: where the user finds it (glossary term). en: one plain sentence, at most ~20
  words, describing the effect for the user.

$rules

GLOSSARY (English = Hebrew):
$glossary

COMMITS:
$($numbered -join "`n`n")
"@
        Write-Host "Pass 1 batch $([int]($i / $size) + 1)"
        $r = Invoke-Gemini $p1 $noteSchema { param($o) $null -ne $o.notes }
        if (-not $r) { throw "Gemini failed on pass 1 - no release now; Release retries by itself in an hour." }
        $found += @($r.notes | Where-Object { $_.visible -and $_.section -ne 'none' -and $_.en })
        Start-Sleep -Seconds 5
    }
    $lines = ($found | ForEach-Object { "- $($_.section) | $($_.area_en) | $($_.en)" }) -join "`n"
    Write-Host "Pass 1: $($found.Count) visible commits`n$lines"
    $p2 = @"
$about

Below are one-line summaries of every user-visible commit since the last release
($previousTag), as "section | area | summary". Many are steps of the same feature.

TASK 1 - merge them into the final release notes:
- Merge lines about the same feature into ONE change describing the finished result
  (e.g. ten bookmark lines -> one bookmarks change). Different features stay separate
  changes - do NOT drop a feature because it is small; small polish of one area may be
  combined into one change about that area.
- Cover BOTH products. Expect roughly 12-25 changes for a release this size.
- Re-check the section of each merged change against the rules below.
- area_en / area_he: exact glossary terms. en: one plain sentence, at most ~25 words.
  he: the same in natural, fluent Hebrew as a native Israeli writer would phrase it -
  not a word-for-word translation, no anglicisms, correct spelling, glossary terms.
- rank: 1 = headline, 2 = notable, 3 = minor. Never invent features or numbers.

TASK 2 - the semantic version bump: "major" = something users relied on was removed or
works differently, or old settings/data stop working; "minor" = any new feature or
option; "patch" = only fixes, polish and speed.

$rules

GLOSSARY (English = Hebrew):
$glossary

SUMMARIES:
$lines
"@
    Write-Host "Pass 2"
    $plan = Invoke-Gemini $p2 $planSchema { param($o) @($o.changes).Count -and $o.bump }
} else {
    $prompt = @"
$about

Below are the developer commit messages since the last release ($previousTag). Each has
a subject, an optional body and "Files:" - the source folders it changed.
- Most commits are internal (CI, build scripts, docs, tests, refactors, service
  plumbing): leave them out. A body line "User-facing: ..." is the author's verdict.
- Go through ALL commits, note every user-visible change, then merge steps of one
  feature into ONE change. Cover BOTH products.
- rank: 1 = headline, 2 = notable, 3 = minor. Never invent features or numbers.
- bump: "major" = removed/changed behaviour users relied on; "minor" = any new feature
  or option; "patch" = only fixes, polish, speed.

$rules

GLOSSARY (English = Hebrew):
$glossary

COMMITS:
$commits
"@
    $plan = Invoke-Gemini $prompt $planSchema { param($o) @($o.changes).Count -and $o.bump }
}
if (-not $plan) { throw "Gemini failed on every model - no release now; Release retries by itself in an hour." }
if ($plan.bump -notin 'major', 'minor', 'patch') { throw "Gemini returned bump '$($plan.bump)'" }
Write-Host "Bump: $($plan.bump) - $($plan.reason)"
# Markdown per language from the structured changes: sections in order, rank inside.
function Format-Notes([string]$lang) {
    $heads = if ($lang -eq 'he') { @{ new = 'חדש'; improved = 'שיפורים'; fixed = 'תיקונים' } }
             else { @{ new = 'New'; improved = 'Improved'; fixed = 'Fixed' } }
    $out = foreach ($sec in 'new', 'improved', 'fixed') {
        $items = @($plan.changes | Where-Object section -eq $sec | Sort-Object rank)
        if (-not $items) { continue }
        "### $($heads[$sec])"
        foreach ($c in $items) { "- **$($c."area_$lang")** - $($c.$lang)" }
        ''
    }
    if ($Auto) {
        $out += if ($lang -eq 'he') { '*גרסה זו פורסמה אוטומטית במסגרת השחרור השבועי האוטומטי.*' }
                else { '*This version was published automatically by the weekly automatic release.*' }
    }
    ($out -join "`n").Trim()
}
$plan | Add-Member notes_en (Format-Notes 'en')
$plan | Add-Member notes_he (Format-Notes 'he')

$notes = @"
<div dir="rtl">

$($plan.notes_he.Trim())

</div>

---

$($plan.notes_en.Trim())
"@
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($OutFile, $notes, $utf8)
Write-Host $notes
if ($NotesOnly) { return }

# Each language on its own for the installer's "What's new" page (embedded when present).
$dir = Join-Path $PSScriptRoot '..\..\Build\Installer\ReleaseNotes'
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'notes.he.md'), $plan.notes_he.Trim(), $utf8)
[IO.File]::WriteAllText((Join-Path $dir 'notes.en.md'), $plan.notes_en.Trim(), $utf8)

if ($env:GITHUB_OUTPUT) { "bump=$($plan.bump)" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
