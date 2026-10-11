import type { ServiceUnavailableReason } from '@/webview-host/serviceAvailability'

/** Hebrew texts of ServiceUnavailableBanner.vue, one entry per reason. */
export const SERVICE_UNAVAILABLE_TEXTS: Record<ServiceUnavailableReason, { title: string; body: string }> = {
  NotRunning: {
    title: 'שירות כתבי הקודש אינו מגיב',
    body: 'הספרים והחיפוש אינם זמינים כרגע, כי הם תלויים בשירות זה. ייתכן שהשירות מופעל מחדש - נא ללחוץ על "נסה שוב".',
  },
  NotInstalled: {
    title: 'שירות כתבי הקודש אינו מותקן',
    body: 'הספרייה והחיפוש זקוקים לשירות זה, והוא אינו מותקן במחשב. התקנה מחדש של התוכנה (כלי קודש) תפתור זאת.',
  },
  InstallDeclined: {
    title: 'התקנת השירות לא אושרה',
    body: 'הספרייה והחיפוש זקוקים לשירות כתבי הקודש, והתקנתו דורשת אישור מנהל במחשב. נא ללחוץ על "נסה שוב" ולאשר את חלון ההרשאה.',
  },
  InstallFailed: {
    title: 'התקנת השירות נכשלה',
    body: 'הספרייה והחיפוש זקוקים לשירות כתבי הקודש, אך התקנתו או תיקונו נכשלו. נא לנסות שוב; אם הבעיה חוזרת, יש להתקין את התוכנה מחדש.',
  },
  StartFailed: {
    title: 'לא ניתן להפעיל את השירות',
    body: 'הספרייה והחיפוש זקוקים לשירות כתבי הקודש, אך מערכת ההפעלה אינה מפעילה אותו. נא ללחוץ על "נסה שוב" כדי לתקן (ייתכן שיופיע חלון הרשאה); אם הבעיה חוזרת, יש להפעיל מחדש את המחשב או להתקין את התוכנה מחדש.',
  },
}

export const SERVICE_RETRY_LABEL = 'נסה שוב'
export const SERVICE_RETRYING_LABEL = 'מנסה...'
