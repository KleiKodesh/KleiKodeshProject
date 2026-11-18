<script setup lang="ts">
import { provide, reactive, computed, ref } from 'vue'
import { IconDismiss16Regular } from '@iconify-prerendered/vue-fluent'
import { PANE_NAVIGATION_KEY, type PaneNavigation } from '@/composables/usePaneNavigation'
import { useAppShellPane } from '@/composables/useAppShellPane'
import type { Tab } from '@/stores/tabStore'
import { useWidgetWindowStore, type WidgetWindowState } from './widgetWindowStore'
import { findWidgetItem } from './widgetItems'

const props = defineProps<{ win: WidgetWindowState }>()
const store = useWidgetWindowStore()
// removeCustom closes a widget's window before dropping it, so an open window always has its item.
const item = computed(() => findWidgetItem(props.win.id)!)

// The tool pages were written as tab pages: they read "their" tab (route, saved query)
// and write its title. Inside a widget they get a private stand-in tab, so they behave
// as the active page and never touch the user's real tabs. Anything that opens a real
// document (a book link in the dictionary) still goes to pane 1.
const pane = useAppShellPane(1)
const widgetTab = reactive<Tab>({ id: `widget:${props.win.id}`, title: item.value.text, route: item.value.route })
const navigation: PaneNavigation = {
  updateActiveTab: (patch) => { Object.assign(widgetTab, patch, { route: item.value.route }) },
  openTab: pane.openTab,
  openOrUpdateActiveTab: (patch, openInNewTab) => pane.openOrUpdateActiveTab(patch, openInNewTab),
  openBookTarget: pane.openBookTarget,
  navigateToDestination: pane.navigateToDestination,
  switchTab: pane.switchTab,
  get activeTabId() { return widgetTab.id },
  get activeTab() { return widgetTab },
  get tabs() { return pane.tabs.value },
}
provide(PANE_NAVIGATION_KEY, navigation)

const windowEl = ref<HTMLElement | null>(null)
const MIN_W = 280
const MIN_H = 200

function clampIntoView() {
  const w = props.win
  w.x = Math.min(Math.max(w.x, 40 - w.width), window.innerWidth - 40)
  w.y = Math.min(Math.max(w.y, 0), window.innerHeight - 32)
}

function startDrag(e: PointerEvent) {
  if ((e.target as HTMLElement).closest('button')) return
  const w = props.win
  const startX = e.clientX, startY = e.clientY, ox = w.x, oy = w.y
  const el = e.currentTarget as HTMLElement
  el.setPointerCapture(e.pointerId)
  const move = (ev: PointerEvent) => { w.x = ox + ev.clientX - startX; w.y = oy + ev.clientY - startY; clampIntoView() }
  const up = () => {
    el.removeEventListener('pointermove', move)
    el.removeEventListener('pointerup', up)
    el.removeEventListener('lostpointercapture', up)
  }
  el.addEventListener('pointermove', move)
  el.addEventListener('pointerup', up)
  // Touch cancel or a lost capture ends the gesture too, or the window keeps following.
  el.addEventListener('lostpointercapture', up)
}

// Resize from the bottom corner on the inline-end side (bottom-left in RTL).
function startResize(e: PointerEvent) {
  const w = props.win
  if (w.fitContent) {
    // Take over the size it has now, then let the hand decide.
    const rect = windowEl.value!.getBoundingClientRect()
    w.width = rect.width
    w.height = rect.height
    w.fitContent = false
  }
  const rtl = getComputedStyle(e.currentTarget as HTMLElement).direction === 'rtl'
  const startX = e.clientX, startY = e.clientY, ow = w.width, oh = w.height, ox = w.x
  const el = e.currentTarget as HTMLElement
  el.setPointerCapture(e.pointerId)
  const move = (ev: PointerEvent) => {
    const dx = ev.clientX - startX
    if (rtl) {
      const width = Math.max(MIN_W, ow - dx)
      w.x = ox + (ow - width)
      w.width = width
    } else {
      w.width = Math.max(MIN_W, ow + dx)
    }
    // Never past the screen's bottom or end edge.
    w.height = Math.min(Math.max(MIN_H, oh + ev.clientY - startY), window.innerHeight - w.y)
    if (!rtl) w.width = Math.min(w.width, window.innerWidth - w.x)
    else if (w.x < 0) { w.width += w.x; w.x = 0 }
  }
  const up = () => {
    el.removeEventListener('pointermove', move)
    el.removeEventListener('pointerup', up)
    el.removeEventListener('lostpointercapture', up)
  }
  el.addEventListener('pointermove', move)
  el.addEventListener('pointerup', up)
  // Touch cancel or a lost capture ends the gesture too, or the window keeps following.
  el.addEventListener('lostpointercapture', up)
}

function onKeydown(e: KeyboardEvent) {
  if (e.code === 'Escape') { e.stopPropagation(); store.close(props.win.id) }
}
</script>

<template>
  <section
    ref="windowEl"
    class="widget-window"
    :class="{ 'widget-window--fit': win.fitContent }"
    role="dialog"
    :aria-label="item.text"
    :data-widget="win.id"
    :style="{ left: `${win.x}px`, top: `${win.y}px`, ...(win.fitContent ? {} : { width: `${win.width}px`, height: `${win.height}px` }), zIndex: 300 + win.z }"
    @pointerdown.capture="store.focus(win.id)"
    @keydown="onKeydown"
  >
    <header class="widget-header" @pointerdown="startDrag">
      <component :is="item.icon" class="widget-icon" :style="item.color ? { color: item.color } : {}" />
      <span class="widget-title">{{ item.text }}</span>
      <button class="widget-close" :title="$t('layout.widgets.close')" @click="store.close(win.id)">
        <IconDismiss16Regular />
      </button>
    </header>
    <div class="widget-body">
      <component :is="item.page" v-bind="item.props" />
    </div>
    <div class="widget-resize" @pointerdown.stop="startResize" />
  </section>
</template>

<style scoped>
.widget-window {
  position: fixed;
  display: flex;
  flex-direction: column;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.28);
  overflow: hidden;
}
.widget-header {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  padding-inline: 10px 4px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  cursor: move;
  user-select: none;
  flex-shrink: 0;
}
.widget-icon { width: 16px; height: 16px; flex-shrink: 0; }
.widget-title {
  flex: 1;
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.widget-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 24px;
  background: none;
  border: none;
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
}
.widget-close:hover { background: #c42b1c; color: #fff; }
.widget-body {
  flex: 1;
  min-height: 0;
  position: relative;
  overflow: auto;
  display: flex;
  flex-direction: column;
}
.widget-body > :deep(*) { flex: 1; min-height: 0; }
.widget-window--fit { min-width: 220px; max-width: calc(100vw - 32px); max-height: calc(100vh - 32px); }
.widget-window--fit .widget-body > :deep(*) { flex: none; }
/* PageCard switches to its edge-to-edge look by WINDOW width (@media), which a widget
   never reaches - so a widget always gets the narrow look. */
.widget-body :deep(.page-card-outer) { max-width: 100%; padding: 0; }
.widget-body :deep(.page-card) { border: none; border-radius: 0; background: var(--bg-primary); }
.widget-resize {
  position: absolute;
  bottom: 0;
  inset-inline-end: 0;
  width: 14px;
  height: 14px;
  cursor: nwse-resize;
}
.widget-window:dir(rtl) .widget-resize { cursor: nesw-resize; }
</style>
