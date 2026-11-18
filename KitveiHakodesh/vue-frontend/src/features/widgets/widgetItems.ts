import { defineAsyncComponent, type Component } from 'vue'
import { IconClock24Filled, IconWeatherSunnyLow24Filled, IconBookClock24Filled } from '@iconify-prerendered/vue-fluent'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { t } from '@/i18n'
import type { TabRoute } from '@/stores/tabStore'
import { useWidgetWindowStore, type WidgetId, type CustomWidget } from './widgetWindowStore'

export interface WidgetItem {
  id: WidgetId
  /** The page's route - the widget's stand-in tab carries it so a tab page behaves as "active". */
  route: TabRoute
  text: string
  description: string
  icon: Component
  color: string | undefined
  shortcut: string
  page: Component
  /** Props for `page` (a custom widget's file). */
  props?: Record<string, unknown>
  /** A widget the user added - the menu offers to remove it. */
  custom?: boolean
}

/** A former tab page (dictionary, calendar, halachic units): text and icon shared with the nav tables. */
function pageWidget(id: WidgetId & DocumentIconKey, route: TabRoute, shortcut: string, page: Component): WidgetItem {
  const icon = documentIcon(id)
  return {
    id, route, shortcut, page,
    text: t(`layout.nav.${id}.label`),
    description: t(`layout.nav.${id}.description`),
    icon: icon.icon24,
    color: icon.color || undefined,
  }
}

/** A widget born from the home date bar. It reads no route; home is a placeholder. */
function barWidget(id: WidgetId, icon: Component, color: string, shortcut: string, page: Component): WidgetItem {
  return {
    id, route: '/', shortcut, page, icon, color,
    text: t(`layout.widgets.${id}.label`),
    description: t(`layout.widgets.${id}.description`),
  }
}

/** The widgets & tools menu, in order. Ctrl+Shift+1..6 toggle them. */
export const WIDGET_ITEMS: WidgetItem[] = [
  pageWidget('dict', '/dictionary', 'Ctrl+Shift+1', defineAsyncComponent(() => import('@/features/dictionary/DictionaryPage.vue'))),
  pageWidget('calendar', '/hebrew-calendar', 'Ctrl+Shift+2', defineAsyncComponent(() => import('@/features/hebrew-calendar/HebrewCalendarPage.vue'))),
  pageWidget('ruler', '/midot', 'Ctrl+Shift+3', defineAsyncComponent(() => import('@/features/halachic-units/HalachicUnitsPage.vue'))),
  barWidget('worldClock', IconClock24Filled, '#2b88d8', 'Ctrl+Shift+4', defineAsyncComponent(() => import('./WorldClockWidget.vue'))),
  barWidget('zmanim', IconWeatherSunnyLow24Filled, '#e3a21a', 'Ctrl+Shift+5', defineAsyncComponent(() => import('./ZmanimWidget.vue'))),
  barWidget('learning', IconBookClock24Filled, '#c1440e', 'Ctrl+Shift+6', defineAsyncComponent(() => import('./DailyLearningWidget.vue'))),
]

const CustomHtmlWidget = defineAsyncComponent(() => import('./CustomHtmlWidget.vue'))

function customWidgetItem(widget: CustomWidget): WidgetItem {
  const icon = documentIcon('html')
  return {
    id: widget.id,
    route: '/html-view',
    text: widget.name,
    description: widget.filePath,
    icon: icon.icon24,
    color: icon.color || undefined,
    shortcut: '',
    page: CustomHtmlWidget,
    props: { filePath: widget.filePath },
    custom: true,
  }
}

/** Built-in widgets, then the ones the user added. */
export function allWidgetItems(): WidgetItem[] {
  return [...WIDGET_ITEMS, ...useWidgetWindowStore().customWidgets.map(customWidgetItem)]
}

export function findWidgetItem(id: WidgetId): WidgetItem | undefined {
  return allWidgetItems().find((w) => w.id === id)
}
