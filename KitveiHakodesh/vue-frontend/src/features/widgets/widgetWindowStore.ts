import { defineStore } from 'pinia'
import { ref } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

/**
 * Widgets & tools: small tools (dictionary, calendar, halachic units, and the home date
 * bar's world clock, zmanim and daily learning) that open in a
 * floating window over the app instead of taking over a tab. One window per widget;
 * opening an open one brings it to the front.
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
  x: number
  y: number
  width: number
  height: number
  z: number
  /**
   * Sized to its content (the small date-bar widgets): no empty space, no scrollbar
   * unless the content outgrows the screen. Resizing by hand turns it off.
   */
  fitContent: boolean
}

const FIT_CONTENT = new Set<WidgetId>(['worldClock', 'zmanim', 'learning'])
const CUSTOM_SIZE = { width: 480, height: 560 }
const DEFAULT_SIZE: Record<BuiltinWidgetId, { width: number; height: number }> = {
  dict: { width: 400, height: 560 },
  calendar: { width: 440, height: 560 },
  ruler: { width: 400, height: 520 },
  worldClock: { width: 300, height: 340 },
  zmanim: { width: 300, height: 480 },
  learning: { width: 380, height: 420 },
}

export const useWidgetWindowStore = defineStore('widgetWindows', () => {
  const windows = ref<WidgetWindowState[]>([])
  // Last place each widget sat, so reopening puts it back where the user left it.
  const lastBounds = new Map<WidgetId, Omit<WidgetWindowState, 'id' | 'z'>>()
  let topZ = 1

  function open(id: WidgetId): void {
    const existing = windows.value.find((w) => w.id === id)
    if (existing) { focus(id); return }
    const size = id.startsWith('custom:') ? CUSTOM_SIZE : DEFAULT_SIZE[id as BuiltinWidgetId]
    const width = Math.min(size.width, window.innerWidth - 32)
    const height = Math.min(size.height, window.innerHeight - 80)
    const cascade = windows.value.length * 28
    const bounds = lastBounds.get(id) ?? {
      width,
      height,
      x: Math.max(16, (window.innerWidth - width) / 2 + cascade),
      y: Math.max(48, (window.innerHeight - height) / 2 + cascade),
    }
    windows.value.push({ id, fitContent: FIT_CONTENT.has(id), ...bounds, z: ++topZ })
  }

  function toggle(id: WidgetId): void {
    if (windows.value.some((w) => w.id === id)) close(id)
    else open(id)
  }

  function focus(id: WidgetId): void {
    const w = windows.value.find((win) => win.id === id)
    if (w && w.z !== topZ) w.z = ++topZ
  }

  function close(id: WidgetId): void {
    const w = windows.value.find((win) => win.id === id)
    if (w) lastBounds.set(id, { x: w.x, y: w.y, width: w.width, height: w.height, fitContent: w.fitContent })
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
