# Build — Installer Build System

One-installer pipeline: builds the WPF installer + NSIS wrapper for AnyCPU (the only variant since 2026-10-11).

## Quick Start

```powershell
Build\build-menu.bat
```

Launches interactive menu with options to build, clean, test, or manage GitHub releases.

Regular releases run on **GitHub Actions** (`.github/workflows/release.yml`), which calls the same `build-installer.ps1` — see [GitHub Actions workflows](#github-actions-workflows).

## Folder Structure

```
Build/
├── Installer/              — WPF installer project
│   ├── Helpers/
│   │   ├── AddinInstaller.cs           — Extract VSTO zip, register add-in, version management
│   │   ├── AdminHelper.cs              — UAC elevation & re-launch
│   │   ├── KitveiHakodeshServiceHelper.cs    — Service shutdown/restart for file-locking
│   │   ├── FullSystemCleaner.cs        — Full uninstall cleanup
│   │   ├── KitveiHakodeshHelper.cs     — KitveiHakodesh DB path handling
│   │   ├── SettingsManager.cs          — Registry-backed settings
│   │   ├── ShellRegistrationHelper.cs  — Shell integration / file associations
│   │   └── WordHelper.cs               — Detect/close Word before install
│   ├── Pages/
│   │   ├── LandingPage.xaml     — Welcome screen
│   │   ├── SettingsPage.xaml    — Ribbon settings
│   │   ├── ComponentSettingsPage.xaml — Website whitelist editor
│   │   ├── InstallPage.xaml     — Progress + finish
│   │   └── RepairPage.xaml      — Repair/cleanup
│   ├── Dialogs/
│   │   └── WhitelistEditorDialog.xaml — Edit website list
│   ├── Models/
│   │   └── WhitelistEntry.cs          — Whitelist data model
│   ├── Resources/
│   │   └── InstallerStyles.xaml       — XAML styles
│   └── README.md               — Detailed installer docs
├── nsis/                   — NSIS wrapper
│   └── KleiKodeshWrapper.nsi    — Prereq checks, wraps WPF installer
├── scripts/                — Build orchestration
│   ├── build-installer.ps1     — Main orchestrator (no interactivity; also run by release.yml)
│   ├── build-menu.ps1          — Interactive menu (prompts user)
│   └── build-helpers.ps1       — Shared functions & constants
├── releases/               — Output folder
│   ├── KleiKodeshSetup-vX.Y.Z.exe          (AnyCPU, the only installer)
│   └── KitveiHakodeshPortable-vX.Y.Z.zip   (standalone demo app)
├── build-menu.bat         — Entry point (launches PowerShell menu)
└── RELEASE_NOTES.txt      — Release notes for -ReleaseNotesSource file/both (Gemini writes the root RELEASE_NOTES.txt)
```

## Build Flow

```
build-menu.bat  ──┐
                  ├─→ build-menu.ps1 (prompts: version patch/minor/major/manual/auto,
                  │                   notes commits/file/both/ai, FTS reset, clean install)
                  │   └─ auto/ai → .github/scripts/ai-release-plan.ps1 (needs GEMINI_API_KEY)
                  │
                  └─→ build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits
                      ├─ drop Vue build stamp + *.tsbuildinfo, wipe KleiKodeshVsto\bin\Release + DemoApp bin\Release
                      ├─ UpdateVersion.ps1 (AddinInstaller.cs + csproj + DemoApp AssemblyInfo.cs; re-checked)
                      ├─ restore: msbuild -t:restore, nuget restore (packages.config), npm ci (CI) / npm install
                      ├─ clean solution (skipped with -NoClean)
                      ├─ fresh 5-year self-signed add-in signing certificate (Update-SigningCertificate)
                      ├─ npm run build (Vue) → service native-AOT publish (needs hb-mirror.json)
                      ├─ installer csproj targets: BuildVsto → PackVstoPayload → dotnet build (AnyCPU)
                      ├─ makensis /DPRODUCT_VERSION=vX.Y.Z /DWPF_EXE_PATH=...
                      │  └─ KleiKodeshSetup-vX.Y.Z.exe
                      ├─ MSBuild KitveiHakodeshDemoApp (Release|AnyCPU)
                      │  └─ Zip bin\Release\ → KitveiHakodeshPortable-vX.Y.Z.zip
                      └─ unless -NoRelease: gh release create (replaces an existing same tag) + upload EXE + ZIP,
                         then dispatch website "release-changed" + run release-issue-check.yml + drive-upload.yml
```

Other switches: `-ManualVersion vX.Y.Z`, `-DeleteFtsIndex`, `-ForceCleanInstall`.

**`hb-mirror.json`** (`KitveiHakodesh/CSharpBackend/KitveiHakodesh.Core/HebrewBooks/hb-mirror.json`) is gitignored (it holds the R2 keys). The service publish fails without it (`RequireHebrewBooksMirrorConfig` target) — copy it onto the build machine; CI writes it from the `HB_MIRROR_JSON` secret.

## Build Output

Single build run produces one installer and one portable zip:

| Output | Description |
|--------|-------------|
| `KleiKodeshSetup-vX.Y.Z.exe` | NSIS installer wrapping the WPF installer (AnyCPU, supports both 32-bit and 64-bit Word) |
| `KitveiHakodeshPortable-vX.Y.Z.zip` | Standalone KitveiHakodesh app — no installation needed, run `כתבי הקודש.exe` directly |

Both files are placed in `Build/releases/` and uploaded to the GitHub release.

> Only the AnyCPU installer exists; the x64 and x86 variants were removed on 2026-10-11.

## GitHub Actions Workflows

All in `.github/workflows/`. Secrets: `GEMINI_API_KEY`, `HB_MIRROR_JSON`, `WEBSITE_DISPATCH_TOKEN` (plus the built-in `GITHUB_TOKEN`). No repository variables are used.

| Workflow | Trigger | What it does | Secrets | Output |
|----------|---------|--------------|---------|--------|
| `release.yml` | Daily cron `0 9 * * *` (UTC) + manual (`gh workflow run release.yml`) | Jobs `gate` → `plan` → `release` (+ `retry-later`). See below. | `GEMINI_API_KEY`, `HB_MIRROR_JSON`, `WEBSITE_DISPATCH_TOKEN` | GitHub release `vX.Y.Z` with installer EXE + portable ZIP; dry run → `release-files` artifact |
| `rewrite-release-notes.yml` | Manual, input `tag` | Runs `ai-release-plan.ps1 -Tag` (commits between that release and the one before) and replaces the release body | `GEMINI_API_KEY` | Edited release notes |
| `release-issue-check.yml` | Release `published` + manual (input `tag`); also dispatched by `build-installer.ps1` | Compares commit messages since the previous release with each open issue (Gemini, one issue per request); comments where a commit likely fixes it or adds a useful diagnostic option. Max 30 comments. | `GEMINI_API_KEY` | Bot comments on issues |
| `notify-website.yml` | Release published/unpublished/edited/deleted/released/prereleased | `repository_dispatch` `release-changed` to `KleiKodesh/kleikodesh.github.io` so its download button follows "latest" | `WEBSITE_DISPATCH_TOKEN` | — |
| `drive-upload.yml` ("Drive sync") | Release published/edited/deleted/unpublished; dispatched by `build-installer.ps1` (CI releases fire no release events); weekly cron `0 3 * * 0`; manual (input `check_only`) | rclone-syncs the shared Google Drive folder (`FOLDER_ID` in the workflow): root = latest release's installer + portable zip + notes as Google Docs (Hebrew `מה חדש - vX`, English `Release notes - vX`, split at the `---` line), `גרסה קודמת/` = the release before it; older files go to the Drive trash, other files untouched. The weekly run only compares installer names and syncs on a mismatch (also keeps the Google login used; Google expires one unused 6 months). On failure opens an issue with step-by-step re-login instructions. Google OAuth client: Cloud project `kleikodesh-release-clone` (kleikodeshproject@gmail.com). | `RCLONE_GDRIVE_TOKEN`, `RCLONE_GDRIVE_CLIENT_ID`, `RCLONE_GDRIVE_CLIENT_SECRET` | Drive folder contents |
| `issue-clarity-check.yml` | Issue `opened` + manual (input `issue`) | Gemini fixes a vague title, adds type + area labels (allow-listed), posts one comment: summary or missing-detail questions, likely duplicates | `GEMINI_API_KEY` | Title edit, labels, bot comment |
| `close-stale-issues.yml` | Daily cron `0 3 * * *` + manual | `actions/stale`: issues idle 90 days get `stale` + a warning, closed (not planned) 14 days later. PRs untouched. | — | Labels / closed issues |

All Gemini calls use `gemini-flash-latest`, then `gemini-flash-lite-latest` (free tier; Pro is not used).

### release.yml in detail

- **`gate`** (ubuntu): a manual first attempt always goes. Scheduled runs and automatic retries go only on a permitted Israel date — not Friday/Saturday, not Yom Tov or erev Yom Tov (hebcal, Israel calendar; hebcal unreachable = no release) — and only if there are commits since the last release and (scheduled) that release is at least 7 days old. If the last release is within 60 days of its 5-year signing-certificate expiry, it forces a renewal release (patch, commit notes, no Gemini) even with no new commits.
- **`plan`** (ubuntu): `.github/scripts/ai-release-plan.ps1` — Gemini picks the bump (`auto`) and writes the notes: `RELEASE_NOTES.txt` (Hebrew first, RTL block, then English) and `Build/Installer/ReleaseNotes/notes.he.md` + `notes.en.md` for the installer's "What's new" page; uploaded as the `release-notes` artifact. Scheduled runs add a "weekly automatic release" line. **No AI notes = no build.**
- **`retry-later`**: if `plan` fails, waits an hour and re-dispatches the workflow with the same inputs (`attempt` + 1, up to 24 attempts).
- **`release`** (windows): checks out the repo + `KleiKodesh/EasyTabs` into `ChromeTabs/`, sets up MSBuild, `choco install vstor2010 nsis`, writes `hb-mirror.json` from the secret, downloads the notes, then runs `Build/scripts/build-installer.ps1 -NoClean` with the resolved bump / notes source (`ai` → `file`).
- Manual inputs: `version_increment` (auto/patch/minor/major), `manual_version`, `release_notes` (ai/commits/file/both, default ai), `delete_fts_index`, `force_clean_install`, `dry_run`; `attempt` and `auto` are internal.
- `concurrency: release` — one release run at a time.

## Version Management

**Single source of truth:** `Build/Installer/Helpers/AddinInstaller.cs`

```csharp
public const string Version = "v8.2.1";
```

`UpdateVersion.ps1` syncs this to the csproj `<Version>` tag during every build. Do not edit version anywhere else — see `.kiro/steering/version-management.md` for full details, registry keys, and update checker flow.

## Installation Flow

1. **NSIS wrapper** checks prerequisites (Windows 10+, .NET 4.7.2+, VSTO runtime)
2. **WPF installer** extracts embedded VSTO zip to `%LocalAppData%\KleiKodesh\`
3. Registers add-in with Word + adds to VSTO trusted locations
4. Saves version to registry: `HKCU\SOFTWARE\KleiKodesh\Version`
5. **On update:** Caches preserved (website whitelist, Word→PDF conversions, Bloom filter index) — see `.kiro/steering/cache-preservation-on-update.md`

CLI args (`--silent`, `--repair`, `--wait-for-pid <PID>`) handled in `App.xaml.cs` for auto-update and elevation workflows.

## Website Whitelist

Installer includes a **ComponentSettingsPage** for customizing the website list before installation.

**Source:** `KleiKodeshVsto/WebSitesLib/WebSitesLib/WebSitesWhitelist.json`

Default list embedded in installer zip. User customizations written back to `%LocalAppData%\KleiKodesh\WebSitesWhitelist.json` on install. See `Build/Installer/README.md` for extraction rules.

## Elevation & UAC

- **WPF installer manifest:** `asInvoker` — never forces UAC
- **NSIS wrapper:** `RequestExecutionLevel user` — never forces UAC
- **Repair page:** Shows blue "Run as administrator" button when not elevated; clicking re-launches with `runas` verb

Full details in `.kiro/steering/version-management.md` ("UAC / Elevation Policy" section).

## Dependencies

- **NSIS 3.08+** (for `KleiKodeshWrapper.nsi`)
- **Visual Studio** (MSBuild, Office/VSTO tools; local scripts use VS 18 Community)
- **Node.js / npm** (Vue frontend), **nuget** (packages.config restore)
- **GitHub CLI `gh`** (release upload; skipped when missing or not logged in)
- **PowerShell 5+**
- **.NET Framework 4.7.2+ SDK**
