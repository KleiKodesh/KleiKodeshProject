<div align="center">

<img width="100" alt="כלי קודש לוגו" src="https://github.com/user-attachments/assets/0d528a83-c124-47de-bfcc-ceb87342a2e0" />

# KleiKodesh — כלי קודש

תוסף Microsoft Word לכתיבת מסמכי תורה ומחקר ספרים.

</div>

## פרויקטים

| תיקייה                                                                                               | סוג                   | מטרה                                                  |
| ---------------------------------------------------------------------------------------------------- | --------------------- | ----------------------------------------------------- |
| [`KleiKodeshVstoInstallerWpf`](KleiKodeshVstoInstallerWpf/README.md)                                 | WPF (.NET)            | **האפליקציה הראשית** — מתקינה את תוסף VSTO ב-Word     |
| [`KleiKodeshVsto`](KleiKodeshVsto/README.md)                                                         | VSTO (.NET Framework) | תוסף Word — סרגל כלים, חלוניות משימה, כל הכלים        |
| [`DocSeferLib`](DocSeferLib/README.md)                                                               | ספריית מחלקות WPF     | כלי עיצוב מסמכי תורה (עיצוב תורני)                   |
| [`Kezayit`](Kezayit/README.md)                                                                       | Vue 3 + TypeScript    | ממשק צפייה בספרים (רץ בתוך WebView2)                  |
| [`Kezayit/CSharpBackend/KezayitLib`](Kezayit/CSharpBackend/KezayitLib/README.md)                     | ספריית .NET           | צד שרת C# המחבר את אפליקציית Vue ל-Windows APIs       |
| [`Kezayit/CSharpBackend/BloomSearchEngineLib`](Kezayit/CSharpBackend/BloomSearchEngineLib/README.md) | ספריית .NET           | מנוע חיפוש טקסט מלא מבוסס Bloom filter לספרים         |
| [`kleikodesh.github.io`](kleikodesh.github.io/README.md)                                             | HTML/CSS/JS סטטי      | אתר הפרויקט הציבורי ודף ההורדה                        |

## ארכיטקטורה

```
המשתמש מריץ את המתקין
        ↓
KleiKodeshVstoInstallerWpf  ──מחלץ ורושם──▶  KleiKodeshVsto (תוסף Word)
                                                        │
                              ┌─────────────────────────┤
                              │                         │
                        כפתורי סרגל              חלוניות משימה
                              │
              ┌───────────────┼───────────────┐
              │               │               │
          Kezayit         DocSeferLib      RegexFind / WebSites
       (Vue ב-WebView2)  (עיצוב WPF)      (HTML / WPF)
              │
        KezayitLib (צד שרת C#)
              │
    BloomSearchEngineLib + מסד נתונים SQLite
```

## בנייה

### בנייה לגרסת הפצה (מתקין)

השתמש בתפריט הבנייה האינטראקטיבי:

```
Build\build-menu.bat
```

או קרא ישירות לסקריפט התזמור:

```powershell
# הגדל גרסת patch וצור GitHub release
.\Build\build-installer.ps1 -VersionIncrement patch -ReleaseNotesSource commits

# הגדר גרסה מדויקת, ללא GitHub release
.\Build\build-installer.ps1 -ManualVersion v3.5.0 -NoRelease

# בנייה מהירה לבדיקה — ללא שינוי גרסה, ללא ניקוי, ללא הפצה
.\Build\build-installer.ps1 -ManualVersion v3.2.0 -NoRelease -NoClean
```

צינור הבנייה:
1. מעדכן את הגרסה ב-`AddinInstaller.cs` וב-`KleiKodeshVstoInstallerWpf.csproj`
2. `dotnet build` לבניית מתקין ה-WPF (אירוע הפרה-בילד שלו בונה את תוסף ה-VSTO דרך MSBuild)
3. `makensis.exe` עוטף הכל לקובץ `Build/releases/KleiKodeshSetup-vX.Y.Z.exe`
4. אופציונלית יוצר GitHub release דרך כלי ה-`gh` CLI

### בנייה לפיתוח (MSBuild)

- **פתרון מלא:**
  ```
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" KleiKodeshProject.slnx /m /nologo /verbosity:minimal
  ```
- **פרויקטים מסוג SDK בלבד (dotnet):**
  ```
  dotnet build
  ```
  עובד עבור `KleiKodeshVstoInstallerWpf`, `KezayitLib`, `BloomSearchEngineLib`.  
  פרויקטים ישנים (`KleiKodeshVsto`, `DocSeferLib`) דורשים MSBuild מ-Visual Studio.

## גרסה

גרסת האפליקציה מוגדרת ב-`KleiKodeshVstoInstallerWpf/Helpers/AddinInstaller.cs` כ-`const string Version`.  
כל שאר חותמות הגרסה (`.csproj`, NSIS) נגזרות ממנה על ידי `KleiKodeshVstoInstallerWpf/UpdateVersion.ps1` בזמן הבנייה.  
לאחר ההתקנה היא נכתבת לרג'יסטרי ב-`HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`.
