import { defineStore } from 'pinia'
import { ref } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

/**
 * Widgets & tools: small tools (dictionary, calendar, halachic units, and the home date
 * bar's world clock, zmanim and daily learning) that open in a window of their own
 * (DetachedWindow, the same way a commentary panel does) instead of taking over a tab.
 * One window per widget; opening an open one brings it to the front. Where each window
 * sits and how big it is the host remembers, by the window's name.
 */
export type BuiltinWidgetId = 'dict' | 'calendar' | 'ruler' | 'worldClock' | 'zmanim' | 'learning'
/** A widget the user added: a local HTML page or an Otzaria add-in. */
export type CustomWidgetId = `custom:${string}`
export type WidgetId = BuiltinWidgetId | CustomWidgetId

export interface CustomWidget {
  id: CustomWidgetId
  name: string
  filePath: string
}

const CUSTOM_WIDGETS_KEY = 'widgets.custom'

export interface WidgetWindowState {
  id: WidgetId
  /** Bumped to bring an already open window to the front. */
  focusRequest: number
}

/** Sized to their content: the small date-bar widgets (no empty space, no scrollbar). */
const FIT_CONTENT = new Set<WidgetId>(['worldClock', 'zmanim', 'learning'])
export function widgetFitsContent(id: WidgetId): boolean {
  return FIT_CONTENT.has(id)
}

/**
 * A widget window's first size (the calendar's, which suits every tool); also the size
 * of a panel moved to its own window (DetachedWindow). After that the host keeps the
 * size the user gives it.
 */
export const WIDGET_WINDOW_SIZE = { width: 440, height: 560 }
/** A user-added widget is a whole web page (an Otzaria add-in): it starts larger, at the installer window's size. */
const CUSTOM_SIZE = { width: 640, height: 460 }

export const useWidgetWindowStore = defineStore('widgetWindows', () => {
  const windows = ref<WidgetWindowState[]>([])

  function open(id: WidgetId): void {
    if (windows.value.some((w) => w.id === id)) { focus(id); return }
    windows.value.push({ id, focusRequest: 0 })
  }

  function toggle(id: WidgetId): void {
    if (windows.value.some((w) => w.id === id)) close(id)
    else open(id)
  }

  function focus(id: WidgetId): void {
    const w = windows.value.find((win) => win.id === id)
    if (w) w.focusRequest++
  }

  function close(id: WidgetId): void {
    windows.value = windows.value.filter((win) => win.id !== id)
  }

  function isOpen(id: WidgetId): boolean {
    return windows.value.some((w) => w.id === id)
  }

  // ── User-added widgets ──────────────────────────────────────────────────────
  const customWidgets = ref<CustomWidget[]>(lsGet<CustomWidget[]>(CUSTOM_WIDGETS_KEY) ?? [])

  function addCustom(filePath: string): CustomWidget {
    const existing = customWidgets.value.find((w) => w.filePath === filePath)
    if (existing) return existing
    const widget: CustomWidget = { id: `custom:${Date.now().toString(36)}`, name: widgetNameFromPath(filePath), filePath }
    customWidgets.value = [...customWidgets.value, widget]
    lsSet(CUSTOM_WIDGETS_KEY, customWidgets.value)
    return widget
  }

  function removeCustom(id: CustomWidgetId): void {
    close(id)
    customWidgets.value = customWidgets.value.filter((w) => w.id !== id)
    lsSet(CUSTOM_WIDGETS_KEY, customWidgets.value)
  }

  return { windows, open, toggle, focus, close, isOpen, customWidgets, addCustom, removeCustom }
})

/** The size a widget's window first opens at; after that the host keeps the user's. */
export function widgetWindowSize(id: WidgetId): { width: number; height: number } {
  return id.startsWith('custom:') ? CUSTOM_SIZE : WIDGET_WINDOW_SIZE
}

/**
 * index.html says nothing (every add-in's entry point is called that), so the folder
 * names the widget; any other page is named by its file.
 */
function widgetNameFromPath(filePath: string): string {
  const parts = filePath.split(/[\\/]/).filter(Boolean)
  const file = parts[parts.length - 1] ?? filePath
  const base = file.replace(/\.html?$/i, '')
  return /^index$/i.test(base) && parts.length > 1 ? parts[parts.length - 2]! : base
}
