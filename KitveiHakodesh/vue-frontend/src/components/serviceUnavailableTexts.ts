import type { ServiceUnavailableReason } from '@/webview-host/serviceAvailability'

/** Hebrew texts of ServiceUnavailableBanner.vue, one entry per reason. */
export const SERVICE_UNAVAILABLE_TEXTS: Record<ServiceUnavailableReason, { title: string; body: string }> = {
  NotRunning: {
    title: 'שירות כתבי הקודש אינו מגיב',
    body: 'הספרים והחיפוש אינם זמינים כרגע. ייתכן שהשירות מופעל מחדש - נא ללחוץ על "נסה שוב".',
  },
  NotInstalled: {
    title: 'שירות כתבי הקודש אינו מותקן',
    body: 'הספרים והחיפוש זקוקים לשירות זה, והוא אינו מותקן במחשב. אפשר להתקין אותו עכשיו: לחיצה על "התקן שירות" תתקין אותו, והמחשב יבקש אישור מנהל.',
  },
  InstallDeclined: {
    title: 'התקנת השירות לא אושרה',
    body: 'הספרים והחיפוש זקוקים לשירות כתבי הקודש, והתקנתו דורשת אישור מנהל במחשב. נא ללחוץ על "נסה שוב" ולאשר את חלון ההרשאה.',
  },
  InstallFailed: {
    title: 'התקנת השירות נכשלה',
    body: 'הספרים והחיפוש זקוקים לשירות כתבי הקודש, אך התקנתו או תיקונו נכשלו. נא ללחוץ על "נסה שוב"; אם הבעיה חוזרת, יש להתקין מחדש את התוכנה (כלי קודש).',
  },
  StartFailed: {
    title: 'לא ניתן להפעיל את השירות',
    body: 'שירות כתבי הקודש מותקן, אך מערכת ההפעלה אינה מפעילה אותו. לחיצה על "תקן" תתקן אותו, והמחשב יבקש אישור מנהל.',
  },
}

/** The banner button's label per reason. */
export const SERVICE_ACTION_LABELS: Record<ServiceUnavailableReason, string> = {
  NotRunning: 'נסה שוב',
  NotInstalled: 'התקן שירות',
  InstallDeclined: 'נסה שוב',
  InstallFailed: 'נסה שוב',
  StartFailed: 'תקן',
}

export const SERVICE_RETRYING_LABEL = 'מנסה...'

/** Banner texts for a seforim DB left mid-update by Otzaria (hot journal beside the file). */
export const SEFORIM_DB_UPDATE_INTERRUPTED_TEXTS = {
  title: 'עדכון ספריית אוצריא לא הושלם',
  body: 'עדכון מאגר הספרים של אוצריא נקטע באמצע, ולכן הספרים אינם נטענים כרגע. נא לפתוח את אוצריא ולתת לה להשלים את העדכון - הספרייה תיטען כאן מחדש אוטומטית. אין למחוק קבצים בתיקיית הספרייה.',
}
