import type { Component } from 'vue'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'

/**
 * The one list of app destinations, in menu order.
 *
 * Both surfaces that offer them render this list: AppTitleBarNavDropdown (labelled
 * rows) and AppNavSidebar (icons only, label as the tooltip). They must always agree,
 * and every time they were separate copies they drifted. Icons come from the shared
 * table (utils/documentIcons) for the same reason - the home tiles and the tab strip
 * read it too.
 *
 * The label IS the routing key: useAppNavigation maps it to a route, so renaming one
 * here without renaming it there silently breaks that destination.
 */
export interface AppNavItem {
  label: string
  icon: Component
  /** Undefined means "no explicit colour" - the icon inherits the surrounding text colour. */
  color: string | undefined
  shortcut: string
  /** One short line on what the destination is for (tooltip / home card subtitle). */
  description: string
}

function navItem(label: string, iconKey: DocumentIconKey, shortcut: string, description: string): AppNavItem {
  const icon = documentIcon(iconKey)
  return { label, icon: icon.icon24, color: icon.color || undefined, shortcut, description }
}

export const APP_NAV_ITEMS: AppNavItem[] = [
  navItem('קטלוג הספרים', 'library', 'Ctrl+1', 'עיון בספרים לפי קטגוריות'),
  navItem('חיפוש', 'search', 'Ctrl+2', 'חיפוש טקסט מלא בכל הספרים'),
  navItem('היברו-בוקס', 'hbooks', 'Ctrl+3', 'ספרים סרוקים מאתר היברו-בוקס'),
  navItem('פתח קובץ', 'folder', 'Ctrl+4', 'פתיחת קובץ או תיקייה מהמחשב'),
  navItem('חיפוש קבצים', 'fileSearch', 'Ctrl+5', 'חיפוש מסמכים במחשב'),
  navItem('מילון', 'dict', 'Ctrl+6', 'פירושי מילים בעברית וארמית'),
  navItem('לוח שנה', 'calendar', 'Ctrl+7', 'תאריך עברי וזמני היום'),
  navItem('מידות ושיעורים', 'ruler', 'Ctrl+8', 'המרת מידות ושיעורים הלכתיים'),
]

/**
 * Workspaces is deliberately NOT in the list above, and has no route at all: it is not a
 * destination but a picker, so its button opens `WorkspaceMenu` in place. It lives in the
 * title bar beside home and nowhere else - not on the rail, not in this menu. Adding it
 * back here would give it a tab again.
 */

/** Settings sits below a divider in both surfaces, so it is not part of the list above. */
export const APP_NAV_SETTINGS_ITEM: AppNavItem = navItem('הגדרות', 'settings', 'F1', 'התאמת התוכנה להעדפותיך')
