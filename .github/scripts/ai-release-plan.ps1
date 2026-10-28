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
# still leaves the next (Pro has a zero free-tier quota - don't use it). Explicit names:
# the *-latest aliases moved to models that time out. Lite judges the commit batches;
# the merge passes try the stronger NOTES_MERGE_MODEL first (default gemini-3.5-flash,
# 'none' = lite only) - it merges and sorts sections best, but is often overloaded.
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
    if ($env:NOTES_INPUT -eq 'plain') { "### $msg" } else { "### $msg`nFiles: $($areas -join ', ')" }
}
$commits = ($blocks -join "`n`n").Trim()
if (-not $commits) { throw "No commits since $previousTag - nothing to release." }
Write-Host "Previous release: $previousTag ($($hashes.Count) commits, $(@($blocks).Count) sent)"

$blocks = @($blocks)
# map (default): pass 1 judges commits in small batches so none are skipped, pass 2
# merges the per-commit lines into the notes. single: one prompt with all commits.
$mode = if ($env:NOTES_MODE) { $env:NOTES_MODE } else { 'map' }

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
- "fixed" = something that went wrong before (wrong text, lost or shifted position,
  scrambled copy, failed print, wrong text direction, blank screen) and now does not.
  A commit whose subject says fix / no longer / instead of refusing / now works /
  correctly / keeps the same text in place / always right-to-left is "fixed", even if
  it also reads like an improvement: the old behaviour was wrong (text drifting or
  shifting on resize/zoom, wrong direction in the English interface, wobbling).
- Saving edits into the file, a new keyboard shortcut, a new download -> "new".

WORDING:
- Describe the effect a user sees, never the mechanism. Bad: "custom scrollbar",
  "native indexing", "filter pills", "multi-select", "virtualized rendering", "character scaling",
  "cache", "lazy load". Good: "the scrollbar shows which section you are in", "results
  appear almost at once", "filter definitions by source", "letter stretch widens the
  letters themselves".
- Never mention internal names: WindowedScroller, TanStack, Lucene, FTS, FtsLib, Core,
  service, LinesTable, localStorage, IndexedDB, MessagePack, Vue, VSTO, COM, ref, flush,
  cache, quota, schema, op, API, Fluent, pills, stream, chunk, index, native, render,
  "TOC" (say table of contents), "lines pane", hashes, file names, pixel/ms numbers.
- Never mention version numbers, certificates, signing or security of the release
  itself - the reader already knows which version this is.
- Word add-in changes (ribbon, panes, installer settings, About, macros, interface
  language) get area "Klei Kodesh (Word add-in)" or the exact ribbon tool name
  (e.g. "Torani Design"), never "Settings" (Settings is the library app's page).
  Never put library-app things (book toolbar, update row, search, book view) under the
  Word add-in area, and never make a catch-all item that lists unrelated areas.
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
    # Same area next to each other, so steps of one feature sit together.
    $found = @($found | Sort-Object area_en, section)
    $lines = (0..($found.Count - 1) | ForEach-Object { "[$_] $($found[$_].section) | $($found[$_].area_en) | $($found[$_].en)" }) -join "`n"
    Write-Host "Pass 1: $($found.Count) visible commits`n$lines"
    $p2 = @"
$about

Below are one-line summaries of every user-visible commit since the last release
($previousTag), as "[number] section | area | summary". Many are steps of the same
feature. Account for EVERY number: each goes into "from" of the change that covers it,
or into "omitted" ONLY when it is pure cosmetic trivia (a bar a few pixels slimmer, a
row height, a colour fade). Speed-ups, fixes and anything a reader would notice are
never omitted - merge them into a change instead.

TASK 1 - merge them into the final release notes:
- Merge lines about the same feature into ONE change describing the finished result
  (e.g. ten bookmark lines -> one bookmarks change). Different features stay separate
  changes - do NOT drop a feature because it is small.
- Never merge a fix with an improvement: a fixed line stays in "fixed" (its own change,
  or merged only with other fixes of the same area); likewise "new" stays "new".
- One change = one idea a reader can take in at a glance: at most two related facts,
  never a list of four things. Leave out cosmetic trivia (slightly slimmer bars, row
  heights, fading effects) unless nothing else covers that area.
- Cover BOTH products. Aim for 14-20 changes.
- Re-check the section of each merged change against the rules below.
- area_en / area_he: exact glossary terms. en: one plain sentence, at most ~20 words.
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
    $mergeSchema = $planSchema.Clone()
    $mergeSchema.properties = $planSchema.properties.Clone()
    $mergeSchema.properties.omitted = @{ type = 'ARRAY'; items = @{ type = 'INTEGER' } }
    $mergeSchema.properties.changes = @{ type = 'ARRAY'; items = $changeSchema.Clone() }
    $mergeSchema.properties.changes.items.properties = $changeSchema.properties.Clone()
    $mergeSchema.properties.changes.items.properties.from = @{ type = 'ARRAY'; items = @{ type = 'INTEGER' } }
    $mergeSchema.properties.changes.items.required = $changeSchema.required + 'from'
    $mergeSchema.properties.changes.items.propertyOrdering = @('from') + $changeSchema.propertyOrdering
    $mergeSchema.required = $planSchema.required + 'omitted'
    $mergeSchema.propertyOrdering = @('changes', 'omitted', 'bump', 'reason')
    $merge = if ($env:NOTES_MERGE_MODEL) { $env:NOTES_MERGE_MODEL } else { 'gemini-3.5-flash' }
    if ($merge -ne 'none') { $models = @($merge -split ',' | ForEach-Object Trim) + $models }
    Write-Host "Pass 2"
    $plan = Invoke-Gemini $p2 $mergeSchema { param($o) @($o.changes).Count -and $o.bump }
    # Coverage check: lines neither merged nor knowingly omitted get one more look.
    if ($plan) {
        # Fixes and new features are never trivia: omitting one counts as missing it.
        # Neither is a whole area: an omitted line whose area no change mentions is missed.
        $areasIn = @($plan.changes | ForEach-Object { $_.area_en.ToLowerInvariant() })
        $kept = @($plan.omitted | Where-Object { $_ -ge 0 -and $_ -lt $found.Count -and
            $found[$_].section -eq 'improved' -and $found[$_].area_en.ToLowerInvariant() -in $areasIn })
        $used = @($plan.changes | ForEach-Object { $_.from }) + $kept
        $missed = @(0..($found.Count - 1) | Where-Object { $_ -notin $used })
        Write-Host "Pass 2: $(@($plan.changes).Count) changes, $(@($plan.omitted).Count) omitted, $($missed.Count) unaccounted"
        if ($missed.Count) {
            $have = ($plan.changes | ForEach-Object { "- $($_.section) | $($_.area_en) | $($_.en)" }) -join "`n"
            $left = ($missed | ForEach-Object { "[$_] $($found[$_].section) | $($found[$_].area_en) | $($found[$_].en)" }) -join "`n"
            $p3 = @"
$about

These release notes are already written:
$have

These commit summaries are not covered yet (fixes and new features must appear unless
the notes above already say the same thing):
$left

Return the COMPLETE updated list of changes: every change above (keep its feature),
with each uncovered summary either merged into the change about the same feature or
area (rewrite that sentence to include it, still one plain idea) or added as a new
change. Numbers that are pure cosmetic trivia go into "omitted". Aim for 15-24
changes in total. bump/reason: repeat "$($plan.bump)" and "covered".

$rules

GLOSSARY (English = Hebrew):
$glossary
"@
            Write-Host "Pass 3 (coverage)"
            $before = @($plan.changes).Count
            $extra = Invoke-Gemini $p3 $mergeSchema { param($o) @($o.changes).Count -ge [Math]::Ceiling($before * 0.6) }
            if ($extra) { $plan.changes = @($extra.changes); Write-Host "Pass 3: $before -> $(@($extra.changes).Count) changes" }
        }
    }
    # Pass 4: an editor's read of the finished list - sections, jargon, one idea per item.
    if ($plan) {
        $draft = ($plan.changes | ForEach-Object { "- $($_.section) | $($_.area_en) | $($_.en)" }) -join "`n"
        $p4 = @"
$about

You are the editor. Below is the draft English list of release-note changes, as
"section | area | sentence". Return EXACTLY the same $(@($plan.changes).Count) items in the same
order - never merge, split, drop or add items. For each item you may only:
- move it to the right section using the rules below;
- rewrite the sentence so it says what the reader sees, in plain words: no mechanism or
  jargon (pills, panes, rendering, native, index, cache, flyout, tooltip, task pane);
  when it crams in more than two things, keep the overall effect and drop details;
- keep area_en exactly as in the draft.
- en: one plain sentence, at most ~20 words. he: the same in natural, fluent Hebrew as
  a native Israeli writer would phrase it - not a word-for-word translation, no
  anglicisms, correct spelling, exact glossary terms.
- rank: 1 = headline, 2 = notable, 3 = minor.
- bump: keep "$($plan.bump)" unless a section change makes it wrong; reason: one line.

$rules

GLOSSARY (English = Hebrew):
$glossary

DRAFT:
$draft
"@
        Write-Host "Pass 4 (editor)"
        $n = @($plan.changes).Count
        $edited = Invoke-Gemini $p4 $planSchema { param($o) @($o.changes).Count -eq $n -and $o.bump -and
            # Lite sometimes slips an Arabic look-alike letter into Hebrew words.
            -not (@($o.changes | ForEach-Object { $_.he + $_.area_he }) -match '[\u0600-\u06FF]') }
        if ($edited) { $plan = $edited }
    }
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
# Area names straight from the glossary when the English one is a glossary term, so the
# Hebrew area is always the app's own word (and the English casing matches).
$terms = @{}
foreach ($l in ($glossary -split "`r?`n")) {
    if ($l -match '^\s*([^#=][^=]*?)\s*=\s*(.+?)\s*$') {
        $en = ($Matches[1] -replace '\s*\(.*?\)', '').Trim()
        $terms[$en.ToLowerInvariant()] = @($en, ($Matches[2] -replace '\s*\(.*?\)', '').Trim())
    }
}
foreach ($c in $plan.changes) {
    $t = $terms[(($c.area_en -replace '\s*\(.*?\)', '').Trim()).ToLowerInvariant()]
    if ($t) { $c.area_en = $t[0]; $c.area_he = $t[1] }
}
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
