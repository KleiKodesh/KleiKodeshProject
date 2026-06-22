<div align="center">

<img width="100" alt="כלי קודש לוגו" src="https://github.com/user-attachments/assets/0d528a83-c124-47de-bfcc-ceb87342a2e0" />

# תוסף כלי קודש לוורד 

ארגז כלים לעורך התורני

</div>

## מה התוסף כולל

- **כתבי הקודש** — גישה למאגר הספרים של זית ישירות בתוך וורד
- **חיפוש רגקס** — חיפוש והחלפה עם ביטויים רגולריים מעבר ליכולות וורד
- **עיצוב תורני** — כלים לעיצוב מסמכים כפי שמקובל בספרי קודש
- **קורא קיוויקס** - קורא קבצי זים (אתרי ויקי ללא חיבור לאינטרנט)
- **דרך האתרים** — גישה נוחה לאתרים תורניים (נקדנית, דיקטה, אוצר החכמה ועוד) ישירות מוורד

## פרויקטים

| תיקייה                                                                                               | סוג                   | מטרה                                                  |
| ---------------------------------------------------------------------------------------------------- | --------------------- | ----------------------------------------------------- |
| [`Build/Installer`](Build/Installer/README.md)                                                       | WPF (.NET)            | המתקין — מתקין את תוסף VSTO ב-Word                    |
| [`KleiKodeshVsto`](KleiKodeshVsto/README.md)                                                         | VSTO (.NET Framework) | **פרויקט התוסף הראשי**                                |
| [`KleiKodeshVsto/DocDesign`](KleiKodeshVsto/DocDesign/README.md)                                     | WPF Class Library     | כלים לעיצוב מסמכים תורניים בוורד                      |
| [`KleiKodeshVsto/RegexInWord/RegexFindLib`](KleiKodeshVsto/RegexInWord/RegexFindLib/README.md)       | WPF Class Library     | חיפוש והחלפה עם ביטויים רגולריים — ממשק WPF מקורי    |
| [`KleiKodeshVsto/WebSitesLib`](KleiKodeshVsto/WebSitesLib/README.md)                                 | WPF Class Library     | דפדפן אתרים תורניים עם WebView2                       |
| [`KleiKodeshVsto/Kiwix`](KleiKodeshVsto/Kiwix/README.md)                                           | WinForms + WebView2   | קורא קבצי ZIM (Kiwix) — WebView2 עם kiwix-js מותאם   |
| [`KleiKodeshVsto/Nakdan`](KleiKodeshVsto/Nakdan/README.md)                                         | .NET Library          | עוזר סימוני קודש — OOXML, Dicta API integration       |
| [`WpfLib`](WpfLib/README.md)                                                                         | WPF Class Library     | כלי WPF משותפים — ViewModelBase, converters, controls |
| [`UpdateCheckerLib`](UpdateCheckerLib/README.md)                                                      | .NET Library          | בדיקת עדכונים מ-GitHub והורדתם                        |
| [`KitveiHakodesh`](KitveiHakodesh/README.md)                                                                       | Vue 3 + TypeScript    | ספרייה לצפייה במאגר הספרים של זית / אוצריא            |
| [`KitveiHakodesh/CSharpBackend/KitveiHakodeshLib`](KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/README.md)                     | .NET Library          | WebView2 host לממשק KitveiHakodesh                           |
| [`KitveiHakodesh/CSharpBackend/Ftslib-Csharp`](KitveiHakodesh/CSharpBackend/Ftslib-Csharp/README.md) | .NET Library          | מנוע חיפוש Bloom filter                               |
| [`KitveiHakodesh/CSharpBackend/DocumentLocator`](KitveiHakodesh/CSharpBackend/DocumentLocator/README.md) | Windows Service + .NET | שירות אינדוקס קבצים NTFS עבור חיפוש מהיר                     |
| [`kleikodesh-website`](kleikodesh-website/README.md)                                             | Static HTML/CSS/JS    | אתר הפרויקט הציבורי ודף ההורדה                        |

## ארכיטקטורה

```
User runs the installer
          ↓
Build/Installer  ──installs──▶  KleiKodeshVsto (Word add-in)
                                        │
                            ┌───────────┤
                            │           │
                     Ribbon buttons  Task panes
                            │
          ┌─────────────────┼──────────────────┬──────────────┐
          │                 │                  │              │
   KitveiHakodesh      DocDesign         RegexFindLib   WebSitesLib
  (Vue in WebView2)  (WPF formatting)    (WPF regex UI)  (WPF + WebView2)
          │
    KitveiHakodeshLib (C# backend)
          │
  FtsLib + SQLite database

  WpfLib ──────────────────────────────▶  shared by all WPF libraries
  UpdateCheckerLib ────────────────────▶  auto-update on Word startup
  Kiwix (WinForms + kiwix-js) ─────────▶  ZIM file reader task pane
```

## בנייה

### דרישות קדם

- **Visual Studio 2022 Community** (עם Office Developer Tools ו-VSTO SDK)
- **.NET Framework 4.7.2+** SDK
- **NSIS 3.08+** (for building installers)
- **PowerShell 5+**
- **Node.js 18+** (for KitveiHakodesh Vue frontend)

### בנייה לגרסת הפצה (מתקין)

השתמש בתפריט הבנייה האינטראקטיבי:

```powershell
Build\build-menu.bat
```

או בעקיפין:

```powershell
cd Build\scripts
.\build-installer.ps1
```

זה יבנה **שלוש גרסאות מתקינות**:
- `KleiKodeshSetup-vX.Y.Z-x64.exe` — 64-bit
- `KleiKodeshSetup-vX.Y.Z-x86.exe` — 32-bit  
- `KleiKodeshSetup-vX.Y.Z.exe` — AnyCPU (auto-detect)

### בנייה לפיתוח

```powershell
# אם אתה משנה את תוסף ה-VSTO בלבד
msbuild KleiKodeshProject.slnx /p:Configuration=Debug /p:Platform=x64

# אם אתה משנה את ממשק KitveiHakodesh (Vue)
cd KitveiHakodesh
npm install
npm run dev
```

### גרסאות בנייה

הבנייה מייצרת שלוש גרסאות מ-run בודד — ראה `.kiro/steering/build-variants.md`:

| Platform | Output |
|----------|--------|
| x64 | `KleiKodeshVsto/bin/Release-x64/` |
| x86 | `KleiKodeshVsto/bin/Release-x86/` |
| AnyCPU | `KleiKodeshVsto/bin/Release/` |

כל פרויקט בשרשרת התלויות חייב להגדיר את שלוש ההגדרות הללו.

## גרסאונות וניהול

- **מקור אמת**: `Build/Installer/Helpers/AddinInstaller.cs` → `const string Version = "vX.Y.Z"`
- **ירשומת חלונות**: `HKCU\SOFTWARE\KleiKodesh\Version`
- **GitHub Releases**: תגים בתבנית `vX.Y.Z`

ראה `.kiro/steering/version-management.md` לפרטים מלאים.

## קידוד ודרכים עבודה

- **קודים WPF**: ראה `.kiro/steering/wpf/wpf-best-practices.md` (MVVM, custom controls, binding, performance)
- **קידוד בכלל**: ראה `.kiro/steering/screaming-architecture.md` (ארכיטקטורה, שמות קבצים, ארגון קבוצות)
- **קידוד תוסף VSTO**: ראה [`KleiKodeshVsto/README.md`](KleiKodeshVsto/README.md)
- **קידוד Vue**: ראה [`KitveiHakodesh/README.md`](KitveiHakodesh/README.md)
- **קידוד Backend**: ראה [`KitveiHakodesh/CSharpBackend/README.md`](KitveiHakodesh/CSharpBackend/README.md)

## זקנות חשמליות

ברוב הקבצים יש תוכן עברי. **עולם UTF-8 ללא BOM** — לעולם אל תשתמש ב-PowerShell `Get-Content`/`Set-Content` להעתקת קבצים (מקלל טקסט).

ראה `.kiro/steering/file-encoding.md` לפרטים מלאים.

## מבנה מערכת הקבצים

```
KleiKodeshProject/
├── Build/                   — מתקין WPF + wrapper NSIS
├── KleiKodeshVsto/          — **הפרויקט הראשי של התוסף**
│   ├── DocDesign/           — עיצוב תורני
│   ├── RegexInWord/         — חיפוש regex
│   ├── WebSitesLib/         — דפדפן אתרים
│   ├── Kiwix/               — קורא ZIM
│   ├── Helpers/             — עוזרים משותפים
│   └── Ribbon/              — סרט כלים וורד
├── WpfLib/                  — כלי WPF משותפים
├── UpdateCheckerLib/        — בדיקת עדכונים
├── KitveiHakodesh/          — ספרייה לצפייה בספרים
│   └── CSharpBackend/       — backend C# (WebView2 host, חיפוש)
├── kleikodesh-website/      — אתר ציבורי
└── hebrew-typing-tutor/     — פרויקט נוסף (מטוטור הקלדה)
```

ראה [`project-overview.md`](.kiro/steering/project-overview.md) לתיאור מלא של כל פרויקט.

## AI Agent Navigation Guide

This section helps AI agents find the right file for a given change without searching.

### Common Changes — VSTO Add-in

| Goal | Start Here |
|------|------------|
| Add a ribbon button | `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.xml` (button XML) + `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.cs` (click handler) + `KleiKodeshVsto/Ribbon/RibbonSettingsControl.cs` (visibility toggle) |
| Change task pane behavior | `KleiKodeshVsto/Helpers/TaskpaneManager.cs` — creates/reuses panes |
| Change startup/shutdown logic | `KleiKodeshVsto/ThisAddIn.cs` — entry point |
| Change a tool's UI | The tool's own library: `DocDesignLib/`, `RegexFindLib/`, `WebSitesLib/`, `KiwixLib/` |
| Change shared WPF styles | `WpfLib/Themes/OfficePalette.xaml` — merged palette for all task panes |
| Change version number | `Build/Installer/Helpers/AddinInstaller.cs` — `const string Version = "vX.Y.Z"` |
| Change auto-update logic | `UpdateCheckerLib/UpdateChecker.cs` — GitHub release check + download |
| Change installer pages | `Build/Installer/Pages/` — LandingPage, SettingsPage, InstallPage, etc. |

### Common Changes — KitveiHakodesh (Vue Frontend)

| Goal | Start Here |
|------|------------|
| Add a new page/feature | Create folder in `KitveiHakodesh/vue-frontend/src/features/`, add `*Page.vue`, register in `layout/AppPageView.vue`, add nav entry in `features/home/HomePage.vue` and `layout/AppTitleBarNavDropdown.vue` |
| Add a new SQL query | Add to `KitveiHakodesh/vue-frontend/src/webview-host/queries.sql.ts` (never inline SQL) |
| Add a new setting | Add key to `utils/persistence.ts` `KEYS`, add reactive ref + watcher in `stores/settingsStore.ts` |
| Add a new store | Create in `KitveiHakodesh/vue-frontend/src/stores/` following Pinia pattern |
| Change a feature's UI | Edit that feature's folder under `features/` |
| Change data fetching | `webview-host/db.ts` — routes queries to C# host or Vite dev middleware |
| Change C#/JS bridge | `webview-host/bridge.ts` + `CSharpBackend/KitveiHakodeshLib/Bridge/JsBridge.cs` |
| Change theme system | `theme/themeStore.ts`, `theme/themes.json`, `theme/theme.css` |
| Change PDF viewer | `features/pdf-viewer/` |
| Change search | `features/full-text-search/` (frontend), `CSharpBackend/Ftslib-Csharp/FtsLib/` (backend) |

### Common Changes — Build System

| Goal | Start Here |
|------|------------|
| Change build pipeline | `Build/scripts/build-installer.ps1` — orchestrator |
| Change installer logic | `Build/Installer/Helpers/AddinInstaller.cs` — extract, register, version |
| Change NSIS wrapper | `Build/nsis/KleiKodeshWrapper.nsi` — prerequisite checks, uninstall |
| Change release process | `Build/scripts/build-helpers.ps1` — `New-GitHubRelease` function |
| Add new platform variant | See `.kiro/steering/build-variants.md` — must define output paths in every `.csproj` in the chain |

### Key Entry Points

- **VSTO add-in entry**: `KleiKodeshVsto/ThisAddIn.cs` — startup/shutdown
- **Ribbon entry**: `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.cs` — button click dispatch
- **Vue app entry**: `KitveiHakodesh/vue-frontend/src/main.ts` — app bootstrap
- **Vue app root**: `KitveiHakodesh/vue-frontend/src/App.vue` — root component
- **C# backend entry**: `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/AppViewer.cs` — WebView2 host
- **WPF installer entry**: `Build/Installer/App.xaml.cs` — CLI arg handling + startup
- **NSIS wrapper entry**: `Build/nsis/KleiKodeshWrapper.nsi` — installer wrapper






