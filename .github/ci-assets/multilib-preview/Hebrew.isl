; Hebrew (RTL) messages for the multi-library preview updater. Only the messages the
; wizard actually shows are translated; anything missing falls back to the English default.
[LangOptions]
LanguageName=עברית
LanguageID=$040D
LanguageCodePage=0
DialogFontName=Segoe UI
DialogFontSize=9
WelcomeFontName=Segoe UI
WelcomeFontSize=12
TitleFontName=Segoe UI
TitleFontSize=29
CopyrightFontName=Segoe UI
CopyrightFontSize=8
RightToLeft=yes

[Messages]
SetupAppTitle=התקנה
SetupWindowTitle=התקנה - %1
ButtonBack=&הקודם
ButtonNext=&הבא
ButtonInstall=&עדכן
ButtonOK=אישור
ButtonCancel=ביטול
ButtonYes=&כן
ButtonYesToAll=כן ל&הכול
ButtonNo=&לא
ButtonNoToAll=ל&א להכול
ButtonFinish=&סיום
ExitSetupTitle=יציאה מההתקנה
ExitSetupMessage=העדכון לא הושלם. אם תצא עכשיו, הוא לא יוחל.%n%nאפשר להריץ אותו שוב בכל עת.%n%nלצאת?
ErrorTitle=שגיאה
SetupAborted=העדכון לא הושלם.%n%nתקן את הבעיה והרץ שוב.
WizardReady=מוכן לעדכון
ReadyLabel1=העדכון מוכן להתחיל.
ReadyLabel2a=לחץ על "עדכן" כדי להמשיך, או על "הקודם" כדי לשנות הגדרות.
ReadyLabel2b=לחץ על "עדכן" כדי להמשיך.
WizardInstalling=מעדכן
InstallingLabel=נא להמתין, העדכון מוחל על התקנת כתבי הקודש...
StatusExtractFiles=מחלץ קבצים...
FinishedHeadingLabel=העדכון הושלם
FinishedLabelNoIcons=העדכון הוחל על התקנת כתבי הקודש.
FinishedLabel=העדכון הוחל על התקנת כתבי הקודש.
ClickFinish=לחץ על "סיום" כדי לצאת.

[CustomMessages]
NotFound=לא נמצאה התקנה של "כתבי הקודש" הרשמי.%n%nהתקן קודם את כתבי הקודש הרשמי, ואז הרץ שוב את העדכון.
Applying=מחליף קבצים ומפעיל מחדש את השירות...
Failed=העדכון נכשל (קוד %1). לא שונה דבר, או שבוצע שחזור אוטומטי.%n%nסגור את Word ואת כתבי הקודש ונסה שוב. פרטים ביומן: %2
WelcomeTitle=עדכון ריבוי ספריות לכתבי הקודש
WelcomeBody=זוהי גרסת תצוגה לא רשמית, שנבנתה מהפורק של YairDaniel11 ואינה גרסה רשמית של פרויקט כלי קודש.%n%nהעדכון מוסיף תמיכה בספריות נוספות להתקנה הרשמית הקיימת. הוא מחליף רק את שירות כתבי הקודש, ספריית KitveiHakodeshLib וממשק המשתמש, ואינו נוגע בתוסף של Word, בהגדרות, בנתונים, במטמון ובאינדקסים.%n%nלפני ההחלפה נשמר גיבוי של הקבצים שיוחלפו, בתיקייה שליד תיקיית ההתקנה, ואפשר לשחזר ממנו.%n%nמומלץ לסגור את Word לפני שממשיכים.
FinishNote=הגיבוי נשמר בתיקייה KleiKodesh.multilib-backup-... שליד תיקיית ההתקנה (תיקיית LocalAppData). לשחזור הרץ את restore.ps1 שבתוכה.