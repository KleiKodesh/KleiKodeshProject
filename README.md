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
| [`KleiKodeshVsto/Kiwix`](KleiKodeshVsto/Kiwix/KIWIX_CHANGES.md)                                     | WinForms + JS         | קורא קבצי ZIM (Kiwix) — WebView2 עם kiwix-js מותאם   |
| [`WpfLib`](WpfLib/README.md)                                                                         | WPF Class Library     | כלי WPF משותפים — ViewModelBase, converters, controls |
| [`UpdateCheckerLib`](UpdateCheckerLib/UpdateChecker.cs)                                              | .NET Library          | בדיקת עדכונים מ-GitHub והורדתם                        |
| [`KitveiHakodesh`](KitveiHakodesh/README.md)                                                                       | Vue 3 + TypeScript    | ספרייה לצפייה במאגר הספרים של זית / אוצריא            |
| [`KitveiHakodesh/CSharpBackend/KitveiHakodeshLib`](KitveiHakodesh/CSharpBackend/KitveiHakodeshLib/README.md)                     | .NET Library          | WebView2 host לממשק KitveiHakodesh                           |
| [`KitveiHakodesh/CSharpBackend/FtsLib`](KitveiHakodesh/CSharpBackend/FtsLib/README.md) | .NET Library          | מנוע חיפוש Bloom filter                               |
| [`kleikodesh.github.io`](kleikodesh.github.io/README.md)                                             | Static HTML/CSS/JS    | אתר הפרויקט הציבורי ודף ההורדה                        |

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

### בנייה לגרסת הפצה (מתקין)

השתמש בתפריט הבנייה האינטראקטיבי:

```
Build\build-menu.bat
```






