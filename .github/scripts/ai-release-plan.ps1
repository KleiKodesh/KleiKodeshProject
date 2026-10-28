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
$models = if ($env:GEMINI_MODEL) { @($env:GEMINI_MODEL) } else { @('gemini-flash-latest', 'gemini-flash-lite-latest') }

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
    $areas = @(git diff-tree --no-commit-id --name-only -r $h |
        ForEach-Object { $p = @($_ -split '/'); if ($p.Count -gt 1) { $p[0..([Math]::Min(4, $p.Count - 2))] -join '/' } else { $_ } } |
        Sort-Object -Unique | Select-Object -First 6)
    if ($env:NOTES_INPUT -eq 'plain') { "### $msg" } else { "### $msg`nFiles: $($areas -join ', ')" }
}
$commits = ($blocks -join "`n`n").Trim()
if (-not $commits) { throw "No commits since $previousTag - nothing to release." }
Write-Host "Previous release: $previousTag ($($hashes.Count) commits, $(@($blocks).Count) sent)"

$glossary = Get-Content (Join-Path $PSScriptRoot 'release-glossary.txt') -Raw -Encoding UTF8

$prompt = @"
You write the "What's new" notes for KleiKodesh, a free Windows app for Torah study:
the "Kitvei Hakodesh" library app (book view with commentaries, full-text search, book
catalog, HebrewBooks scans, dictionary, calendar and daily learning) and the "Klei Kodesh"
Microsoft Word add-in (Torani Design, Dicta Nakdan, Regex Find, Websites, Kiwix).
Readers are Torah scholars and yeshiva students. They are not programmers and do not
care how anything works inside - only what they can now do, what got better, and what
no longer goes wrong.

Below are the developer commit messages since the last release ($previousTag). Each has
a subject, an optional body and "Files:" - the source folders it changed.

HOW TO READ THE COMMITS
- Most commits are internal. Leave a commit out entirely when it is about: CI, GitHub
  Actions, release/build/signing scripts, Drive sync, README/docs/steering, tests,
  refactors, renames, code removal, the service/Core/wire/MessagePack/SQL plumbing,
  memory/caching internals, logging, licence text, suggestions.txt, version bumps.
  Files only under .github, Build, docs, *.md, tests -> internal.
- Speed-ups count only when a user would feel them; describe the effect ("search in a
  large book shows results almost at once"), never the technique.
- "User-facing: new|improved|fixed" in a body = include it in that section.
- Many commits are steps of one feature (e.g. ten commits about bookmarks or the
  scrollbar). Merge them into ONE bullet describing the finished result.
- Never mention internal names: WindowedScroller, TanStack, Lucene, FTS, FtsLib, Core,
  service, LinesTable, localStorage, IndexedDB, MessagePack, Vue, VSTO, COM, shallow
  ref, flush, cache, quota, schema, op, API, hashes, file names, pixel/ms numbers.

TASK 1 - the semantic version bump:
- "major": something users relied on was removed or works in a different way, or old
  settings/data stop working.
- "minor": a new feature or a new option in an existing one.
- "patch": only fixes, polish and speed.
Pick the highest level any included change reaches.

TASK 2 - the notes:
- Sections, in this order, only when they have items: "### New", "### Improved",
  "### Fixed" (Hebrew: "### חדש", "### שיפורים", "### תיקונים").
- Each bullet: "- **Area** - what changed for the user." One sentence, at most ~20 words.
  Area = the place in the app the user would look for it, named exactly as in the
  glossary (e.g. "Book view", "Search", "Word add-in - Torani Design").
- Cover every user-visible feature, in BOTH products - Word add-in changes count as
  much as app changes. Order bullets by how much users will care; at most 7 per
  section; drop the trivia.
- Keep to what the commits say - never invent features or numbers.
- notes_en in English. notes_he: the same items in natural, fluent Hebrew as a native
  Israeli writer would phrase it - not a word-for-word translation, no anglicisms,
  no invented words, correct spelling. Address the user in plural or impersonal form.
- TERMINOLOGY: every app-area, button and feature name MUST use the exact term from the
  glossary below (Hebrew side for notes_he). If something is not in the glossary,
  describe it in plain words rather than coining a name.
- No title line, no version number, no intro or closing sentence.
$(if ($Auto) { '- End BOTH notes_en and notes_he with a final italic line (own paragraph) saying this
  version was published automatically by the weekly automatic release.' })

GLOSSARY (English = Hebrew, as shown in the app):
$glossary

COMMITS:
$commits
"@

$body = @{
    contents         = @(@{ parts = @(@{ text = $prompt }) })
    generationConfig = @{
        responseMimeType = 'application/json'
        responseSchema   = @{
            type       = 'OBJECT'
            properties = @{
                bump     = @{ type = 'STRING'; enum = @('major', 'minor', 'patch') }
                reason   = @{ type = 'STRING' }
                notes_en = @{ type = 'STRING' }
                notes_he = @{ type = 'STRING' }
            }
            required   = @('bump', 'reason', 'notes_en', 'notes_he')
        }
    }
} | ConvertTo-Json -Depth 20

$plan = $null
:models foreach ($model in $models) {
    foreach ($attempt in 1..3) {
        try {
            $resp = Invoke-RestMethod -Method Post -TimeoutSec 180 `
                -Uri "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent" `
                -Headers @{ 'x-goog-api-key' = $env:GEMINI_API_KEY } `
                -ContentType 'application/json; charset=utf-8' `
                -Body ([Text.Encoding]::UTF8.GetBytes($body))
            $plan = $resp.candidates[0].content.parts[0].text | ConvertFrom-Json
            if ($plan.notes_en -and $plan.notes_he) { Write-Host "Model: $model"; break models }
            throw "incomplete answer"
        } catch {
            Write-Host "$model attempt $attempt failed: $($_.Exception.Message)"
            $plan = $null
            if ($attempt -lt 3) { Start-Sleep -Seconds (30 * $attempt) }
        }
    }
}
if (-not $plan) { throw "Gemini failed on every model - no release now; Release retries by itself in an hour." }
if ($plan.bump -notin 'major', 'minor', 'patch') { throw "Gemini returned bump '$($plan.bump)'" }
Write-Host "Bump: $($plan.bump) - $($plan.reason)"
if ($Auto -and $plan.notes_en -notmatch 'automatic') {
    $plan.notes_en = $plan.notes_en.Trim() + "`n`n*This version was published automatically by the weekly automatic release.*"
}

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
