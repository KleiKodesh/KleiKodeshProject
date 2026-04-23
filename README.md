<div align="center">

<img width="100" alt="כלי קודש לוגו" src="https://github.com/user-attachments/assets/0d528a83-c124-47de-bfcc-ceb87342a2e0" />

# תוסף כלי קודש לוורד 

ארגז כלים לעורך התורני

</div>

## מה התוסף כולל

- **כזית** — גישה למאגר הספרים של זית ישירות בתוך וורד
- **חיפוש רגקס** — חיפוש והחלפה עם ביטויים רגולריים מעבר ליכולות וורד
- **עיצוב תורני** — כלים לעיצוב מסמכים כפי שמקובל בספרי קודש
- **קורא קיוויקס** - קורא קבצי זים (אתרי ויקי ללא חיבור לאינטרנט)
- *דרך האתרים** — גישה נוחה לאתרים תורניים (נקדנית, דיקטה, אוצר החכמה ועוד) ישירות מוורד

## פרויקטים

| תיקייה                                                                                               | סוג                   | מטרה                                                  |
| ---------------------------------------------------------------------------------------------------- | --------------------- | ----------------------------------------------------- |
| [`Build/Installer`](Build/Installer/README.md)                                 | WPF (.NET)            | המתקין — מתקין את תוסף VSTO ב-Word                    |
| [`KleiKodeshVsto`](KleiKodeshVsto/README.md)                                                         | VSTO (.NET Framework) | **פרויקט התוסף הראשי**                                                    |
| [`DocSeferLib`](DocSeferLib/README.md)                                                               | WPF Class Library     | כלים לעיצוב מסמכים תורניים בוורד                      |
| [`Kezayit`](Kezayit/README.md)                                                                       | Vue 3 + TypeScript    | ספרייה לצפייה במאגר הספרים של זית / אוצריא            |
| [`Kezayit/CSharpBackend/KezayitLib`](Kezayit/CSharpBackend/KezayitLib/README.md)                     | .NET Library          | WebView2 host                                         |
| [`Kezayit/CSharpBackend/BloomSearchEngineLib`](Kezayit/CSharpBackend/BloomSearchEngineLib/README.md) | .NET Library          | מנוע חיפוש Bloom filter                               |
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
                        ┌───────────────┼──────────────────┐
                        │               │                  │
                    Kezayit       DocSeferLib     RegexFind / WebSites
               (Vue in WebView2) (WPF formatting)   (HTML / WPF)
                    │
              KezayitLib (C# backend)
                    │
        BloomSearchEngineLib + SQLite database
```

## בנייה

### בנייה לגרסת הפצה (מתקין)

השתמש בתפריט הבנייה האינטראקטיבי:

```
Build\build-menu.bat
```






