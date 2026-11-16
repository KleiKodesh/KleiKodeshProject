import { computed, type Component } from 'vue'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { t } from '@/i18n'
import { shortcutLabel } from '@/shortcuts/shortcutLabel'
import { useHameshivUnreachable } from '@/composables/hameshivReachability'

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
  /** The shortcut's current combination ("Ctrl+1"), read live from the shortcuts store -
   *  the user can change it in settings. '' when there is none. */
  readonly shortcut: string
  /** Its id in shortcuts/shortcutRegistry.ts ('' = none). */
  shortcutId: string
  /** One short line on what the destination is for (tooltip / home card subtitle). */
  description: string
  /** An external site that can't be reached right now - surfaces show a broken-connection badge. */
  unreachable?: boolean
}

/** Gives `item` a live `shortcut` getter. Re-applied after a spread, which copies a getter's
 *  value rather than the getter. */
function withLiveShortcut<T extends Omit<AppNavItem, 'shortcut'>>(item: T): AppNavItem {
  return Object.defineProperty(item, 'shortcut', {
    get: () => (item.shortcutId ? shortcutLabel(item.shortcutId) : ''),
    enumerable: true,
    configurable: true,
  }) as unknown as AppNavItem
}

function navItem(label: string, iconKey: DocumentIconKey, shortcutId: string): AppNavItem {
  const icon = documentIcon(iconKey)
  const text = t(`layout.nav.${iconKey}.label`)
  const description = t(`layout.nav.${iconKey}.description`)
  return withLiveShortcut({ label, text, icon: icon.icon24, color: icon.color || undefined, shortcutId, description })
}

const ALL_NAV_ITEMS: AppNavItem[] = [
  navItem('קטלוג הספרים', 'library', 'go.catalog'),
  navItem('חיפוש', 'search', 'go.search'),
  navItem('היברו-בוקס', 'hbooks', 'go.hebrewBooks'),
  navItem('פתח קובץ', 'folder', 'go.openFile'),
  navItem('חיפוש קבצים', 'fileSearch', 'go.fileSearch'),
  navItem('אזור אישי', 'userSpace', 'go.userSpace'),
  // In development: shown in dev builds only until the user-space index exists.
  ...(import.meta.env.DEV ? [navItem('חיפוש באזור האישי', 'userSpaceSearch', '')] : []),
  navItem('מילון', 'dict', 'go.dictionary'),
  navItem('לוח שנה', 'calendar', 'go.calendar'),
  navItem('מידות ושיעורים', 'ruler', 'go.measures'),
  navItem('המשיב', 'hameshiv', 'go.hameshiv'),
]

/** Label of the external hameshiv.com item. */
export const HAMESHIV_LABEL = 'המשיב'

/** The destinations to show. Reactive so callers that `.value` it keep working. */
const hameshivUnreachable = useHameshivUnreachable()
export const APP_NAV_ITEMS = computed<AppNavItem[]>(() =>
  ALL_NAV_ITEMS.map((item) => item.label === HAMESHIV_LABEL ? withLiveShortcut({ ...item, unreachable: hameshivUnreachable.value }) : item))

/**
 * Workspaces is deliberately NOT in the list above, and has no route at all: it is not a
 * destination but a picker, so its button opens `WorkspaceMenu` in place. It lives in the
 * title bar beside home and nowhere else - not on the rail, not in this menu. Adding it
 * back here would give it a tab again.
 */

/** Settings sits below a divider in both surfaces, so it is not part of the list above. */
export const APP_NAV_SETTINGS_ITEM: AppNavItem = navItem('הגדרות', 'settings', 'go.settings')
