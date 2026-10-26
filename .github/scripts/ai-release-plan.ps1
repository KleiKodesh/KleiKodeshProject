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
param([string]$OutFile = "RELEASE_NOTES.txt", [string]$Tag, [switch]$Auto)

$ErrorActionPreference = 'Stop'
# Hebrew commit messages: read git's output as UTF-8 (Windows PowerShell 5.1 locally).
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
# Tried in order; each model has its own free-tier quota, so a maxed-out first one
# still leaves the second (Pro has a zero free-tier quota - don't use it).
# No AI notes = no release (the step fails, nothing is built).
$models = if ($env:GEMINI_MODEL) { @($env:GEMINI_MODEL) } else { @('gemini-flash-latest', 'gemini-flash-lite-latest') }

$tags = @(gh release list --limit 200 --exclude-drafts --json tagName --jq '.[].tagName')   # newest first
$previousTag = if ($Tag) { $tags[[array]::IndexOf($tags, $Tag) + 1] } else { $tags[0] }
$end     = if ($Tag) { $Tag } else { 'HEAD' }
$range   = if ($previousTag) { "$previousTag..$end" } else { "-30" }
$commits = (git log $range --no-merges --pretty=format:"### %s%n%b" | Out-String).Trim()
if (-not $commits) { throw "No commits since $previousTag - nothing to release." }
Write-Host "Previous release: $previousTag"

$prompt = @"
You write the release notes for KleiKodesh, a Windows app for Torah study: a Microsoft
Word add-in plus the standalone "Kitvei Hakodesh" library app (book viewer, full-text
search, HebrewBooks downloads, dictionary, Nakdan vocalization, tools for Word documents).
Users are Torah scholars and students, not programmers.

Below are the developer commit messages since the last release ($previousTag).

Task 1 - pick the semantic version bump:
- "major": any breaking change, in the code OR the UI - something that no longer works
  the way it did, was removed or redesigned, or makes old settings/data/integrations
  incompatible.
- "minor": feature updates - a new feature, or a new option/capability in an existing one.
- "patch": only bug fixes or minor tinkering (small tweaks, polish, performance, internal work).
Pick the highest level that any single commit reaches.

Task 2 - write polished release notes, like a professional app's "What's new":
- Only what a user would notice. Drop CI, build, refactors, tests, code moves, docs.
- Merge related commits into one item. Plain friendly language, no jargon, no commit hashes.
- Markdown. Use only the sections that have items, in this order:
  "### New", "### Improved", "### Fixed". One short line per bullet.
- notes_en in English. notes_he is the same content in natural Hebrew (not a word-for-word
  translation), with the section headings in Hebrew too.
- Hebrew quality: write correct, fluent Israeli Hebrew with no misspellings or invented
  words. Re-read every Hebrew line before answering. Use these exact terms:
  Word add-in = תוסף לוורד, Kitvei Hakodesh app = תוכנת כתבי הקודש, book view = תצוגת ספר,
  full-text search = חיפוש בטקסט מלא, catalog = קטלוג, dictionary = מילון,
  Nakdan = נקדן, HebrewBooks = היברובוקס, PDF viewer = מציג PDF, installer = תוכנת ההתקנה,
  settings = הגדרות, toolbar = סרגל כלים, commentaries = מפרשים, tab = לשונית.
- Scope each item to the right part of the app. Commit subjects start with the area
  ("Book view (lines):", "Word add-in:" ...) - keep that area in the note. A commit body
  line "User-facing: none" means leave that commit out entirely; "User-facing: new/
  improved/fixed" tells you the section.
- No title line and no version number - those are added separately.
$(if ($Auto) { '- End BOTH notes_en and notes_he with a final italic line (own paragraph) saying this
  version was published automatically by the weekly automatic release.' })

Commits:
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
            $resp = Invoke-RestMethod -Method Post -TimeoutSec 120 `
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

# Each language on its own for the installer's "What's new" page (embedded when present).
$dir = Join-Path $PSScriptRoot '..\..\Build\Installer\ReleaseNotes'
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'notes.he.md'), $plan.notes_he.Trim(), $utf8)
[IO.File]::WriteAllText((Join-Path $dir 'notes.en.md'), $plan.notes_en.Trim(), $utf8)

if ($env:GITHUB_OUTPUT) { "bump=$($plan.bump)" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
