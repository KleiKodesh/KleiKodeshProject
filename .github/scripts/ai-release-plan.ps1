# Asks Gemini to read the commits since the last release and decide
#   1. the version bump (major / minor / patch)
#   2. user-facing release notes, in English and Hebrew
# Writes the notes to RELEASE_NOTES.txt (build-installer.ps1 -ReleaseNotesSource file
# picks them up) and the bump to $env:GITHUB_OUTPUT as "bump=...".
#
# Env: GEMINI_API_KEY, GH_TOKEN. Optional: GEMINI_MODEL.
param([string]$OutFile = "RELEASE_NOTES.txt")

$ErrorActionPreference = 'Stop'
$model = if ($env:GEMINI_MODEL) { $env:GEMINI_MODEL } else { 'gemini-flash-latest' }

$previousTag = gh release list --limit 1 --exclude-drafts --json tagName --jq '.[0].tagName'
$range   = if ($previousTag) { "$previousTag..HEAD" } else { "-30" }
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
- "major": a big user-visible overhaul or a breaking change in how users work.
- "minor": at least one new user-facing feature or capability.
- "patch": only fixes, polish, performance or internal work.

Task 2 - write polished release notes, like a professional app's "What's new":
- Only what a user would notice. Drop CI, build, refactors, tests, code moves, docs.
- Merge related commits into one item. Plain friendly language, no jargon, no commit hashes.
- Markdown. Use only the sections that have items, in this order:
  "### New", "### Improved", "### Fixed". One short line per bullet.
- notes_en in English. notes_he is the same content in natural Hebrew (not a word-for-word
  translation), with the section headings in Hebrew too.
- No title line and no version number - those are added separately.

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

foreach ($attempt in 1..4) {
    try {
        $resp = Invoke-RestMethod -Method Post -TimeoutSec 300 `
            -Uri "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent" `
            -Headers @{ 'x-goog-api-key' = $env:GEMINI_API_KEY } `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body))
        break
    } catch {
        if ($attempt -eq 4) { throw }
        Write-Host "Gemini attempt $attempt failed: $($_.Exception.Message) - retrying"
        Start-Sleep -Seconds (15 * $attempt)
    }
}

$plan = $resp.candidates[0].content.parts[0].text | ConvertFrom-Json
if ($plan.bump -notin 'major', 'minor', 'patch') { throw "Gemini returned bump '$($plan.bump)'" }
Write-Host "Bump: $($plan.bump) - $($plan.reason)"

$notes = @"
$($plan.notes_en.Trim())

---

<div dir="rtl">

$($plan.notes_he.Trim())

</div>
"@
[IO.File]::WriteAllText($OutFile, $notes, (New-Object Text.UTF8Encoding($false)))
Write-Host $notes

if ($env:GITHUB_OUTPUT) { "bump=$($plan.bump)" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
