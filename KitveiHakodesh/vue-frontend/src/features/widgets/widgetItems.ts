import { defineAsyncComponent, type Component } from 'vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import { IconClock24Filled, IconWeatherSunnyLow24Filled, IconBookClock24Filled, IconLightbulb24Filled, IconInfo24Filled } from '@iconify-prerendered/vue-fluent'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { t } from '@/i18n'
import type { TabRoute } from '@/stores/tabStore'
import { useWidgetWindowStore, CALENDAR_EXTENDED, type WidgetId, type CustomWidget } from './widgetWindowStore'

/** A widget page loaded on first open, the app's loading animation shown meanwhile (from the first moment). */
function lazyPage(loader: Parameters<typeof defineAsyncComponent>[0] & (() => Promise<unknown>)): Component {
  return defineAsyncComponent({ loader: loader as () => Promise<Component>, loadingComponent: LoadingAnimation, delay: 0 })
}

export interface WidgetItem {
  id: WidgetId
  /** The stand-in tab's route: '/html-view' for a custom page (the HTML viewer reads it), '/' otherwise. */
  route: TabRoute
  text: string
  description: string
  /** What else the widget shows, listed under its name in the menu. */
  subtitle?: string
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
function pageWidget(
  id: WidgetId & DocumentIconKey, shortcut: string, page: Component,
  extra?: Pick<WidgetItem, 'subtitle' | 'props'>,
): WidgetItem {
  const icon = documentIcon(id)
  return {
    id, route: '/', shortcut, page, ...extra,
    text: t(`layout.nav.${id}.label`),
    description: t(`layout.nav.${id}.description`),
    icon: icon.icon24,
    color: icon.color || undefined,
  }
}

/** A widget born from the home date bar. */
function barWidget(id: 'worldClock' | 'zmanim' | 'learning', icon: Component, color: string, shortcut: string, page: Component): WidgetItem {
  return {
    id, route: '/', shortcut, page, icon, color,
    text: t(`layout.widgets.${id}.label`),
    description: t(`layout.widgets.${id}.description`),
  }
}

/** The widgets & tools menu, in order. Ctrl+Shift+1..7 toggle them. */
export const WIDGET_ITEMS: WidgetItem[] = [
  pageWidget('dict', 'Ctrl+Shift+1', lazyPage(() => import('@/features/dictionary/DictionaryPage.vue'))),
  pageWidget('calendar', 'Ctrl+Shift+2', lazyPage(() => import('@/features/hebrew-calendar/HebrewCalendarPage.vue')),
    CALENDAR_EXTENDED
      ? { props: { extended: true } }
      : undefined),
  pageWidget('ruler', 'Ctrl+Shift+3', lazyPage(() => import('@/features/halachic-units/HalachicUnitsPage.vue'))),
  barWidget('worldClock', IconClock24Filled, '#2b88d8', 'Ctrl+Shift+4', lazyPage(() => import('./WorldClockWidget.vue'))),
  barWidget('zmanim', IconWeatherSunnyLow24Filled, '#e3a21a', 'Ctrl+Shift+5', lazyPage(() => import('./ZmanimWidget.vue'))),
  barWidget('learning', IconBookClock24Filled, '#c1440e', 'Ctrl+Shift+6', lazyPage(() => import('./DailyLearningWidget.vue'))),
  {
    id: 'keepScreenOn', route: '/', shortcut: 'Ctrl+Shift+7', icon: IconLightbulb24Filled, color: '#f2c811',
    text: t('settings.keepScreenOn.label'),
    description: t('settings.keepScreenOn.hint'),
    page: lazyPage(() => import('./KeepScreenOnWidget.vue')),
  },
]

/** Widgets opened from elsewhere (not in the widgets menu, no shortcut): About, from Settings. */
export const HIDDEN_WIDGET_ITEMS: WidgetItem[] = [
  {
    id: 'about', route: '/', shortcut: '', icon: IconInfo24Filled, color: '#0f6cbd',
    text: t('settings.about.title'),
    description: t('settings.about.appName'),
    page: lazyPage(() => import('@/features/about/AboutPage.vue')),
  },
]

const CustomHtmlWidget = lazyPage(() => import('./CustomHtmlWidget.vue'))

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
  return [...allWidgetItems(), ...HIDDEN_WIDGET_ITEMS].find((w) => w.id === id)
}
