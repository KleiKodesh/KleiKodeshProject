# Asks Gemini to read the commits since the last release and decide
#   1. the version bump (major / minor / patch)
#   2. user-facing release notes, in English and Hebrew
# Writes the notes to RELEASE_NOTES.txt (build-installer.ps1 -ReleaseNotesSource file
# picks them up) and the bump to $env:GITHUB_OUTPUT as "bump=...".
#
# Env: GEMINI_API_KEY, GH_TOKEN. Optional: GEMINI_MODEL, NOTES_MERGE_MODEL (comma lists).
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
# still leaves the next (Pro has a zero free-tier quota - don't use it). Explicit names:
# the *-latest aliases moved to models that time out. Lite judges the commit batches;
# merging and Hebrew try the stronger NOTES_MERGE_MODEL first (default gemini-3.5-flash,
# 'none' = lite only) - it merges best, but is often overloaded.
# No AI notes = no release (the step fails, nothing is built).
$models = if ($env:GEMINI_MODEL) { @($env:GEMINI_MODEL -split ',' | ForEach-Object Trim) } else { @('gemini-3.5-flash-lite', 'gemini-3.1-flash-lite', 'gemini-flash-lite-latest') }

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
    "### $msg`nFiles: $($areas -join ', ')"
}
$blocks = @($blocks)
if (-not $blocks) { throw "No commits since $previousTag - nothing to release." }
Write-Host "Previous release: $previousTag ($($hashes.Count) commits, $($blocks.Count) sent)"

# Three steps, all in English until the last:
#   1. lite model, 15 commits per call: one verdict per commit (visible? section, area, sentence)
#   2. one call merges the verdicts into changes; the script then fixes each change's
#      section from its commits' verdicts, drops trivia and caps the length
#   3. one call writes the Hebrew for the final list
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
  keyboard shortcut, language, download, page. Saving edits into a file is "new".
- "improved" = something that already existed now works better, faster, clearer, or a
  bit differently (a Torani Design row that now changes letters another way is improved).
- "fixed" = the old behaviour was wrong and now is not: wrong or scrambled text, text
  drifting or shifting on resize/zoom, wrong direction in the English interface,
  wobbling, failed print, blank screen. A commit saying fix / no longer / instead of
  refusing / correctly / always right-to-left is "fixed", even if it reads like an
  improvement.

WORDING:
- Describe the effect a user sees, never the mechanism. Bad: "custom scrollbar",
  "native indexing", "filter pills", "virtualized rendering", "character scaling",
  "cache", "lazy load". Good: "the scrollbar shows which section you are in", "results
  appear almost at once", "filter definitions by source".
- Never mention internal names: WindowedScroller, TanStack, Lucene, FTS, FtsLib, Core,
  service, LinesTable, localStorage, IndexedDB, MessagePack, Vue, VSTO, COM, cache,
  schema, API, Fluent, pills, chunk, index, native, render, flyout, tooltip, "TOC" (say
  table of contents), hashes, file names, pixel/ms numbers, version numbers,
  certificates, signing.
- Word add-in changes (ribbon, panes, installer settings, About, macros, interface
  language) get area "Klei Kodesh" or the exact ribbon tool name (e.g. "Torani Design"),
  never "Settings" (Settings is the library app's page). Library-app things never go
  under the Word add-in area.
- Area names MUST be exact glossary English terms. Anything not in the glossary is
  described in plain words, never given a coined name.

STYLE EXAMPLES (from older releases):
- new | Calendar | Tap a date to see that day's zmanim and daily learning.
- improved | Book catalog | Opening a large category is instant and keeps your scroll place.
- fixed | Tabs | Closing a tab no longer jumps to the first tab.
"@

$glossary = Get-Content (Join-Path $PSScriptRoot 'release-glossary.txt') -Raw -Encoding UTF8
# English area term -> @(English, Hebrew), so area names come from the glossary itself.
$terms = @{}
foreach ($l in ($glossary -split "`r?`n")) {
    if ($l -match '^\s*([^#=][^=]*?)\s*=\s*(.+?)\s*$') {
        $en = ($Matches[1] -replace '\s*\(.*?\)', '').Trim()
        $terms[$en.ToLowerInvariant()] = @($en, ($Matches[2] -replace '\s*\(.*?\)', '').Trim())
    }
}

# The whole run stays well inside the release job's timeout; past this budget the step
# fails and Release retries by itself in an hour.
$clock = [Diagnostics.Stopwatch]::StartNew()
$budgetMinutes = 25

# One generateContent call, trying each model in turn; returns the parsed JSON or $null.
function Invoke-Gemini([string]$text, $schema, [string[]]$chain, [scriptblock]$valid) {
    $body = @{
        contents         = @(@{ parts = @(@{ text = $text }) })
        generationConfig = @{ temperature = 0.2; responseMimeType = 'application/json'; responseSchema = $schema }
    } | ConvertTo-Json -Depth 30
    foreach ($model in $chain) {
        foreach ($attempt in 1..2) {
            if ($clock.Elapsed.TotalMinutes -gt $budgetMinutes) { throw "Gemini took over $budgetMinutes minutes - no release now; Release retries by itself in an hour." }
            try {
                $resp = Invoke-RestMethod -Method Post -TimeoutSec 150 `
                    -Uri "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent" `
                    -Headers @{ 'x-goog-api-key' = $env:GEMINI_API_KEY } `
                    -ContentType 'application/json; charset=utf-8' `
                    -Body ([Text.Encoding]::UTF8.GetBytes($body))
                $out = $resp.candidates[0].content.parts[0].text | ConvertFrom-Json
                if (& $valid $out) { Write-Host "  model: $model"; return $out }
                throw "incomplete answer"
            } catch {
                Write-Host "  $model attempt $attempt failed: $($_.Exception.Message)"
                if ($_.Exception.Message -match '404|429|503|Timeout') { break }   # overloaded or gone - next model
                Start-Sleep -Seconds 15
            }
        }
    }
    $null
}

# --- Step 1: a verdict per commit, in small batches so none is skipped ---
$verdictSchema = @{
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
    $prompt = @"
$about

Below are $($batch.Count) developer commit messages. For EACH commit return one entry with
the same id: is anything in it visible to a user of the app or the Word add-in?
- visible=false (section "none", empty texts) for: CI, GitHub Actions, build/release/
  signing scripts, docs, tests, refactors, renames, code removal, background service
  plumbing, logging, licence text, version bumps, pure internal clean-up.
- visible=true otherwise - including speed-ups a user would feel, small fixes, new
  options, English interface work, installer settings, About page, macros, downloads.
- A body line "User-facing: <section> - <Area>: <sentence>" is the author's own verdict:
  copy it. "User-facing: none" means visible=false.
- area_en: glossary term. en: one plain sentence, at most ~20 words, the effect for the user.

$rules

GLOSSARY (English = Hebrew):
$glossary

COMMITS:
$($numbered -join "`n`n")
"@
    Write-Host "Step 1, batch $([int]($i / $size) + 1)"
    $r = Invoke-Gemini $prompt $verdictSchema $models { param($o) $null -ne $o.notes }
    if (-not $r) { throw "Gemini failed on every model - no release now; Release retries by itself in an hour." }
    $found += @($r.notes | Where-Object { $_.visible -and $_.section -ne 'none' -and $_.en })
}
# Same area next to each other, so steps of one feature sit together.
$found = @($found | Sort-Object area_en, section)
$lines = (0..($found.Count - 1) | ForEach-Object { "[$_] $($found[$_].section) | $($found[$_].area_en) | $($found[$_].en)" }) -join "`n"
Write-Host "Step 1: $($found.Count) visible commits`n$lines"

# --- Step 2: merge into changes (stronger model first, lite when it is overloaded) ---
$merge = if ($env:NOTES_MERGE_MODEL) { $env:NOTES_MERGE_MODEL } else { 'gemini-3.5-flash' }
$mergeChain = @(if ($merge -ne 'none') { $merge -split ',' | ForEach-Object Trim }) + $models
$planSchema = @{
    type       = 'OBJECT'
    properties = @{
        changes = @{ type = 'ARRAY'; items = @{
            type       = 'OBJECT'
            properties = @{
                from    = @{ type = 'ARRAY'; items = @{ type = 'INTEGER' } }
                area_en = @{ type = 'STRING' }
                en      = @{ type = 'STRING' }
                rank    = @{ type = 'INTEGER' }
            }
            required   = @('from', 'area_en', 'en', 'rank')
            propertyOrdering = @('from', 'area_en', 'en', 'rank')
        } }
        bump    = @{ type = 'STRING'; enum = @('major', 'minor', 'patch') }
        reason  = @{ type = 'STRING' }
    }
    required   = @('changes', 'bump', 'reason')
    propertyOrdering = @('changes', 'bump', 'reason')
}
$prompt = @"
$about

Below are one-line summaries of every user-visible commit since the last release
($previousTag), as "[number] section | area | summary". Many are steps of one feature.

TASK 1 - the release notes:
- Merge lines about the same feature into ONE change describing the finished result
  (ten bookmark lines -> one bookmarks change); put their numbers in "from". Different
  features stay separate - never drop a fix or a new feature because it is small, and
  never join unrelated features or areas into one catch-all change.
- Never merge a "fixed" line with non-fixed lines: fixes get their own changes.
- One change = one idea a reader takes in at a glance (at most two related facts).
- Cover BOTH products. Aim for 15-22 changes: merge all speed-ups of one area into one
  change, and all steps of one feature into one change.
- en: one plain sentence, at most ~20 words.
- rank: 1 = headline a reader cares about, 2 = notable, 3 = minor polish.
- Never invent features or numbers.

TASK 2 - the semantic version bump: "major" = something users relied on was removed or
works differently, or old settings/data stop working; "minor" = any new feature or
option; "patch" = only fixes, polish and speed.

$rules

GLOSSARY (English = Hebrew):
$glossary

SUMMARIES:
$lines
"@
Write-Host "Step 2 (merge)"
$plan = Invoke-Gemini $prompt $planSchema $mergeChain { param($o) @($o.changes).Count -and $o.bump }
if (-not $plan) { throw "Gemini failed on every model - no release now; Release retries by itself in an hour." }
if ($plan.bump -notin 'major', 'minor', 'patch') { throw "Gemini returned bump '$($plan.bump)'" }

# Section from the commits' own verdicts, not the merge model: mostly fixes -> fixed;
# a feature born in this release (a third of its commits "new") -> new; else improved.
foreach ($c in $plan.changes) {
    $secs = @($c.from | Where-Object { $_ -ge 0 -and $_ -lt $found.Count } | ForEach-Object { $found[$_].section })
    $fixed = @($secs | Where-Object { $_ -eq 'fixed' }).Count
    $new   = @($secs | Where-Object { $_ -eq 'new' }).Count
    $c | Add-Member section $(if (-not $secs) { 'improved' } elseif ($fixed * 2 -ge $secs.Count) { 'fixed' } elseif ($new * 3 -ge $secs.Count) { 'new' } else { 'improved' })
    $c | Add-Member weight $secs.Count
    $t = $terms[(($c.area_en -replace '\s*\(.*?\)', '').Trim()).ToLowerInvariant()]
    $c | Add-Member area_he $(if ($t) { $t[1] } else { $c.area_en })
    if ($t) { $c.area_en = $t[0] }
}
# Fixes the merge folded into an improvement come back as their own items, one per
# area, in step 1's wording - a fix is what a reader most needs to hear about.
$inFixed = @($plan.changes | Where-Object section -eq 'fixed' | ForEach-Object { $_.from })
$lost = @(0..($found.Count - 1) | Where-Object { $found[$_].section -eq 'fixed' -and $_ -notin $inFixed })
foreach ($g in ($lost | Group-Object { $found[$_].area_en })) {
    $f = $found[$g.Group[0]]
    $t = $terms[(($f.area_en -replace '\s*\(.*?\)', '').Trim()).ToLowerInvariant()]
    $plan.changes = @($plan.changes) + [pscustomobject]@{
        from = @($g.Group); area_en = $(if ($t) { $t[0] } else { $f.area_en }); area_he = $(if ($t) { $t[1] } else { $f.area_en })
        en = $f.en; rank = 2; section = 'fixed'; weight = $g.Count }
}
if ($lost) { Write-Host "Fixes brought back: $($lost.Count) lines" }
# Short and focused: no cosmetic-only areas, at most 8 per section and 20 in all.
# Order of preference: the best change of each area first (so no area vanishes behind
# five speed-ups of another), then the rest; within that, rank, then most commits.
$trivia = 'Theme', 'Status bar', 'Workspaces', 'Context menu', 'Book details', 'Back / Forward'
$sorted = @($plan.changes | Where-Object { -not ($_.area_en -in $trivia -and $_.section -eq 'improved') } |
    Sort-Object rank, @{ Expression = 'weight'; Descending = $true })
$seen = @{}
$firsts = @(); $rest = @()
foreach ($c in $sorted) { if ($seen[$c.area_en]) { $rest += $c } else { $seen[$c.area_en] = 1; $firsts += $c } }
$pick = @(); $perSection = @{ new = 0; improved = 0; fixed = 0 }
foreach ($c in $firsts + $rest) {
    if ($pick.Count -ge 20 -or $perSection[$c.section] -ge 8) { continue }
    $pick += $c; $perSection[$c.section]++
}
$kept = @($pick | Sort-Object rank, @{ Expression = 'weight'; Descending = $true })
Write-Host "Step 2: $(@($plan.changes).Count) changes, $($kept.Count) kept"
$plan.changes = $kept

# --- Step 3: the Hebrew, written from the final English list ---
$heSchema = @{
    type       = 'OBJECT'
    properties = @{ he = @{ type = 'ARRAY'; items = @{ type = 'STRING' } } }
    required   = @('he')
}
$numbered = for ($k = 0; $k -lt $kept.Count; $k++) { "[$k] ($($kept[$k].area_he)) $($kept[$k].en)" }
$prompt = @"
Write the Hebrew release notes for KleiKodesh (a Torah-study app and Word add-in; readers
are yeshiva students and Torah scholars). Below are $($kept.Count) English items, numbered,
each with its area's Hebrew name in brackets. Return "he": exactly $($kept.Count) Hebrew
sentences, in the same order - item [0] first.
- Write as a native Israeli writer would phrase a short app update note: simple, direct,
  natural Hebrew. Say what the user can do or notices. Not a word-for-word translation:
  restructure freely, drop English filler ("easily", "now", "significantly").
- No anglicisms, no invented words, correct spelling and grammar, Hebrew letters only
  (plus PDF, Word, Ctrl and digits where needed).
- Any app area, button or feature name MUST be the exact Hebrew glossary term.
- Do not repeat the area name at the start of the sentence.

GLOSSARY (English = Hebrew):
$glossary

ITEMS:
$($numbered -join "`n")
"@
Write-Host "Step 3 (Hebrew)"
$he = Invoke-Gemini $prompt $heSchema $mergeChain { param($o) @($o.he).Count -eq $kept.Count -and
    # Lite sometimes slips an Arabic look-alike letter into Hebrew words.
    -not (@($o.he) -match '[\u0600-\u06FF]') }
if (-not $he) { throw "Gemini failed on every model - no release now; Release retries by itself in an hour." }
for ($k = 0; $k -lt $kept.Count; $k++) { $kept[$k] | Add-Member he $he.he[$k] }

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
