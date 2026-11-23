import { useTabStore } from '@/stores/tabStore'
import { findWidgetItem } from './widgetItems'
import type { WidgetId } from './widgetWindowStore'

/**
 * Opens a widget as a tab of this window - what a widget window becomes when it is
 * dropped on the tab strip. Unknown ids (a custom widget removed meanwhile) open nothing.
 */
export function openWidgetTab(widgetId: string): void {
  const item = findWidgetItem(widgetId as WidgetId)
  if (!item) return
  useTabStore().openTab({ route: '/widget', title: item.text, widgetId: item.id })
}
