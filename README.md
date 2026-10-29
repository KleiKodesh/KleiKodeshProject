<div align="center">

<img width="100" alt="כלי קודש לוגו" src="https://github.com/user-attachments/assets/0d528a83-c124-47de-bfcc-ceb87342a2e0" />

# תוסף כלי קודש לוורד 

ארגז כלים לעורך התורני

</div>

## מה התוסף כולל

- **כתבי הקודש** — גישה למאגר הספרים של זית / אוצריא ישירות בתוך וורד (וגם כתוכנה עצמאית)
- **חיפוש רגקס** — חיפוש והחלפה עם ביטויים רגולריים מעבר ליכולות וורד
- **עיצוב תורני** — כלים לעיצוב מסמכים כפי שמקובל בספרי קודש
- **נקדן** — ניקוד בעזרת Dicta
- **קורא קיוויקס** - קורא קבצי זים (אתרי ויקי ללא חיבור לאינטרנט)
- **דרך האתרים** — גישה נוחה לאתרים תורניים (נקדנית, דיקטה, אוצר החכמה ועוד) ישירות מוורד

## מבנה המאגר

| תיקייה | סוג | מטרה |
| --- | --- | --- |
| [`Build`](Build/README.md) | WPF + NSIS + PowerShell | המתקין (`Build/Installer`), עטיפת NSIS וסקריפטי הבנייה וההפצה |
| [`KleiKodeshVsto`](KleiKodeshVsto/README.md) | VSTO (.NET Framework 4.8) | **פרויקט התוסף הראשי** — Ribbon, חלוניות, וכלי המשנה שבתוכו (שורות הבאות) |
| [`KleiKodeshVsto/DocDesign`](KleiKodeshVsto/DocDesign/README.md) | WPF Class Library | עיצוב מסמכים תורניים |
| [`KleiKodeshVsto/RegexInWord`](KleiKodeshVsto/RegexInWord/README.md) | WPF Class Library | חיפוש והחלפה ברגקס (`RegexFindLib`) |
| [`KleiKodeshVsto/WebSitesLib`](KleiKodeshVsto/WebSitesLib/README.md) | WPF + WebView2 | דפדפן אתרים תורניים |
| [`KleiKodeshVsto/Kiwix`](KleiKodeshVsto/Kiwix/README.md) | WinForms + WebView2 | קורא קבצי ZIM עם kiwix-js מותאם |
| [`KleiKodeshVsto/Nakdan`](KleiKodeshVsto/Nakdan/README.md) | .NET Library | ניקוד — OOXML + Dicta API |
| [`KitveiHakodesh`](KitveiHakodesh/README.md) | Vue 3 + C# | כתבי הקודש — ממשק Vue, שכבות C# (ראה ארכיטקטורה למטה), סקריפטי נתונים |
| [`ChromeTabs`](ChromeTabs/README.md) | WinForms (.NET 4.8) | `FluentChromeTabs` — לשוניות בסגנון Chrome לתוכנה העצמאית |
| [`WpfLib`](WpfLib/README.md) | WPF Class Library | כלי WPF משותפים לכל החלוניות — ערכת צבעים, פקדים, converters |
| `WpfLib.Gallery` / `WpfLib.Gallery.Tests` | WPF + xUnit | גלריית הפקדים של WpfLib ובדיקות ויזואליות/התנהגות עליה |
| [`SearchExpansion`](SearchExpansion/README.md) | Python | בונה את `expansion-routed.db` — הרחבת מילים בחיפוש הטקסט המלא |
| `SearchExpansion-models` | נתונים (לא במעקב git) | מודלים וקבצי עבודה מקומיים של SearchExpansion |
| `packages` | NuGet | חבילות NuGet של פרויקטי `packages.config` |
| `hebrew-typing-tutor` | Web app (מאגר נפרד, לא במעקב) | מאמן הקלדה עברית |
| `kleikodesh-website` | Static HTML (מאגר נפרד, לא במעקב) | האתר הציבורי ודף ההורדה ([kleikodesh.github.io](https://kleikodesh.github.io)) |

`KleiKodeshProject.slnx` — הפתרון הראשי (תוסף, מתקין, ספריות WPF, KitveiHakodeshLib, FtsLib). שכבות ה-.NET 10 (Core, Service) נבנות מ-`KitveiHakodesh/CSharpBackend/KitveiHakodesh.slnx`.

`hebrew-typing-tutor` ו-`kleikodesh-website` הם מאגרי git עצמאיים שמוחרגים ב-`.gitignore` (אינם submodules ואינם נדרשים לבנייה).

## ארכיטקטורה

```
Build/Installer ──installs──▶ KleiKodeshVsto (Word add-in, net48)
                                 │
       ┌──────────────┬──────────┴───┬──────────────┬─────────┐
   DocDesign     RegexFindLib    WebSitesLib      Kiwix     Nakdan
                                                      (WpfLib shared by the WPF panes)
                                 │
                       KitveiHakodesh pane
```

### KitveiHakodesh — שכבות

```
vue-frontend (Vue 3 UI)          features/html-view also hosts Otzaria addins
   │  MessagePack {op,args}       (window.Otzaria, data-query API only)
   ▼
host (WebView2)                   KitveiHakodeshLib (net48): the VSTO pane and
   │                              KitveiHakodeshDemoApp (standalone app, ChromeTabs)
   │  hosted: relays http://kitvei-service/rpc → named pipe (ServiceRpcRelay)
   │  dev:    browser → loopback HTTP 127.0.0.1 (port via vite /khs-endpoint)
   ▼
KitveiHakodeshService (net10, native AOT, LocalSystem Windows service)
   │  transport only: PipeServer + HttpHostServer → Dispatcher
   ▼
KitveiHakodesh.Core (net10.0-windows, IsAotCompatible) — owns all logic
   │  SeforimDb, catalog, FTS, dictionary, HebrewBooks, local files,
   │  settings, annotations, library download
   ▼
FtsLib (generic full-text engine) · DocConvertLib (Word→HTML) · SQLite
```

- **Core מחזיק את כל הלוגיקה** — כל host צריך להיות מסוגל לעשות הכל דרך Core בלבד; השירות הוא שכבת תעבורה בלבד.
- **Core חייב להיות תואם AOT** — השירות מתפרסם כ-native AOT; אין reflection/יצירת קוד בזמן ריצה. כל טיפוס שעובר בחוט צריך `[MessagePackObject]`.
- `KitveiHakodesh.Core.Portable` — אותם קבצי Core מקומפלים ל-net10.0 נקי (macOS/Linux), לכלי השוואה/מדידה.
- `KitveiHakodeshService.Tests` — בדיקות השירות.

פירוט: [`KitveiHakodesh/CSharpBackend/README.md`](KitveiHakodesh/CSharpBackend/README.md), [`KitveiHakodesh.Core/README.md`](KitveiHakodesh/CSharpBackend/KitveiHakodesh.Core/README.md), `KitveiHakodesh/.kiro/steering/`.

## בנייה

הכל מתועד ב-[`Build/README.md`](Build/README.md). בקצרה:

```powershell
Build\build-menu.bat     # תפריט בנייה אינטראקטיבי
```

- מתקין **אחד** (AnyCPU, `Release|AnyCPU` בלבד) — `KleiKodeshSetup-vX.Y.Z.exe` + `KitveiHakodeshPortable-vX.Y.Z.zip`.
- שחרור גרסה רגיל רץ ב-GitHub Actions (`.github/workflows/release.yml`), שמריץ את אותו `Build/scripts/build-installer.ps1`.
- **מקור אמת לגרסה**: `Build/Installer/Helpers/AddinInstaller.cs` → `const string Version = "vX.Y.Z"` (ראה `.kiro/steering/version-management.md`).

פיתוח:

```powershell
msbuild KleiKodeshProject.slnx /p:Configuration=Debug   # תוסף VSTO

cd KitveiHakodesh/vue-frontend                          # ממשק Vue
npm install
npm run dev
```

## קידוד ודרכי עבודה

- **WPF**: `.kiro/steering/wpf/wpf-best-practices.md`
- **תוסף VSTO**: [`KleiKodeshVsto/README.md`](KleiKodeshVsto/README.md)
- **KitveiHakodesh (Vue + C#)**: `KitveiHakodesh/.kiro/steering/` (canon), [`KitveiHakodesh/README.md`](KitveiHakodesh/README.md)
- **קידוד קבצים**: רוב הקבצים מכילים עברית — UTF-8 ללא BOM; אין להשתמש ב-`Get-Content`/`Set-Content` של PowerShell להעתקת קבצים. ראה `.kiro/steering/file-encoding.md`.
- תיאור מלא של הפרויקטים: [`project-overview.md`](.kiro/steering/project-overview.md)

## AI Agent Navigation Guide

### Common Changes — VSTO Add-in

| Goal | Start Here |
|------|------------|
| Add a ribbon button | `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.xml` + `KeliKodeshRibbon.cs` (click handler) + `RibbonSettingsControl.cs` (visibility toggle) |
| Change task pane behavior | `KleiKodeshVsto/Helpers/TaskpaneManager.cs` |
| Change startup/shutdown logic | `KleiKodeshVsto/ThisAddIn.cs` |
| Change a tool's UI | The tool's own library under `KleiKodeshVsto/` (DocDesign, RegexInWord, WebSitesLib, Kiwix, Nakdan) |
| Change shared WPF styles | `WpfLib/Themes/OfficePalette.xaml` |
| Change installer pages | `Build/Installer/Pages/` |

### Common Changes — KitveiHakodesh

| Goal | Start Here |
|------|------------|
| Add a page/feature | `KitveiHakodesh/vue-frontend/src/features/<feature>/`, register in `layout/AppPageView.vue` |
| Add a setting | `stores/settingsStore.ts` (+ `utils/persistence.ts`) |
| Talk to the backend | `webview-host/serviceClient.ts` (frontend) → `KitveiHakodeshService/Ipc/Dispatcher.cs` (op routing) → logic in `KitveiHakodesh.Core/` |
| SQL on seforim.db | `KitveiHakodesh.Core/SeforimDb/` (`SeforimDbSqlStrings.cs`) |
| Full-text search | `features/full-text-search/` → `KitveiHakodesh.Core/SeforimDbFullTextSearch/` → `FtsLib-Csharp/FtsLib/` |
| Host / WebView2 bridge | `KitveiHakodeshLib/AppViewer.cs`, `KitveiHakodeshLib/Bridge/` (`ServiceRpcRelay.cs`) |
| Theme | `vue-frontend/src/theme/` |

### Common Changes — Build

| Goal | Start Here |
|------|------------|
| Build pipeline | `Build/scripts/build-installer.ps1` |
| Installer logic / version | `Build/Installer/Helpers/AddinInstaller.cs` |
| NSIS wrapper | `Build/nsis/KleiKodeshWrapper.nsi` |
| Release / CI | `.github/workflows/release.yml`; see `Build/README.md` |

### Key Entry Points

- **VSTO add-in**: `KleiKodeshVsto/ThisAddIn.cs`
- **Vue app**: `KitveiHakodesh/vue-frontend/src/main.ts`, `App.vue`
- **WebView2 host**: `KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/AppViewer.cs`
- **Service**: `KitveiHakodesh/CSharpBackend/KitveiHakodeshService/Program.cs`
- **WPF installer**: `Build/Installer/App.xaml.cs`
