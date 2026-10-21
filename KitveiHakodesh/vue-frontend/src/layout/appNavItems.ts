import type { Component } from 'vue'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { t } from '@/i18n'

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
  /** Routing key (Hebrew, never translated). Display with `text`. */
  label: string
  /** The label in the UI language. */
  text: string
  icon: Component
  /** Undefined means "no explicit colour" - the icon inherits the surrounding text colour. */
  color: string | undefined
  shortcut: string
  /** One short line on what the destination is for (tooltip / home card subtitle). */
  description: string
}

function navItem(label: string, iconKey: DocumentIconKey, shortcut: string): AppNavItem {
  const icon = documentIcon(iconKey)
  const text = t(`layout.nav.${iconKey}.label`)
  const description = t(`layout.nav.${iconKey}.description`)
  return { label, text, icon: icon.icon24, color: icon.color || undefined, shortcut, description }
}

export const APP_NAV_ITEMS: AppNavItem[] = [
  navItem('קטלוג הספרים', 'library', 'Ctrl+1'),
  navItem('חיפוש', 'search', 'Ctrl+2'),
  navItem('היברו-בוקס', 'hbooks', 'Ctrl+3'),
  navItem('פתח קובץ', 'folder', 'Ctrl+4'),
  navItem('חיפוש קבצים', 'fileSearch', 'Ctrl+5'),
  navItem('מילון', 'dict', 'Ctrl+6'),
  navItem('לוח שנה', 'calendar', 'Ctrl+7'),
  navItem('מידות ושיעורים', 'ruler', 'Ctrl+8'),
]

/**
 * Workspaces is deliberately NOT in the list above, and has no route at all: it is not a
 * destination but a picker, so its button opens `WorkspaceMenu` in place. It lives in the
 * title bar beside home and nowhere else - not on the rail, not in this menu. Adding it
 * back here would give it a tab again.
 */

/** Settings sits below a divider in both surfaces, so it is not part of the list above. */
export const APP_NAV_SETTINGS_ITEM: AppNavItem = navItem('הגדרות', 'settings', 'F1')
