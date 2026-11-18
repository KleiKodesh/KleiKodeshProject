<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useTabStore } from '@/stores/tabStore'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import SiteLeavePrompt from './SiteLeavePrompt.vue'
import { refreshSiteFrameFocus } from './siteFrameFocus'
import { forgetSiteFrame, reloadSiteFrame, siteFrames, type SiteFrame } from './siteFrames'
import { displayHost, websiteForUrl, websiteName } from './websitesList'

/**
 * Every live website iframe, mounted ONCE here at the app root and never moved (moving an iframe
 * reloads it). Each one is positioned over its tab's placeholder in WebViewPage with CSS anchor
 * positioning (the browser keeps it in place through resizes and split-view drags) and hidden
 * with visibility when its tab is not shown, so a hidden site keeps all its state. Rendered
 * after the panes (an anchor must come first in the document) and below dialogs and menus.
 * See siteFrames.ts for the lifecycle and memory rules.
 */
const tabStore = useTabStore()
const bookViewStore = useBookViewStore()
const liveFrames = computed(() => [...siteFrames.values()].filter((f) => f.live))

const isHosted = typeof window.__webviewAction === 'function'
/** Hosted the iframe loads the site itself; the dev browser goes through vite's dev proxy (websitesDevProxy.ts). */
function frameSrc(frame: SiteFrame): string {
  if (isHosted || !import.meta.env.DEV) return frame.startUrl
  try {
    const target = new URL(frame.startUrl)
    return `http://${target.hostname}.localhost:${Number(location.port || 80) + 1000}${target.pathname}${target.search}${target.hash}`
  } catch {
    return frame.startUrl
  }
}

function frameStyle(frame: SiteFrame): string {
  return [
    `position-anchor: ${frame.anchor}`,
    'top: anchor(top)', 'left: anchor(left)', 'width: anchor-size(width)', 'height: anchor-size(height)',
    `visibility: ${frame.shown ? 'visible' : 'hidden'}`,
  ].join('; ')
}

// A closed tab: its iframe goes. A tab that moved on to another page keeps it (hidden), so Back
// returns to the live site, like a browser's back-forward cache; low memory still unloads it.
watch(
  () => tabStore.tabs.map((tab) => tab.id).join('|'),
  () => {
    const tabIds = new Set(tabStore.tabs.map((tab) => tab.id))
    for (const tabId of [...siteFrames.keys()]) if (!tabIds.has(tabId)) forgetSiteFrame(tabId)
  },
)

const frameOf = (el: Element | null): SiteFrame | undefined =>
  el instanceof HTMLIFrameElement ? siteFrames.get(el.dataset.tabId ?? '') : undefined

// ── Focus: the host takes the app's shortcut keys while a site has focus; the frame's pane becomes
//    the focused one (a click inside the frame never reaches the app document). ──
function onFrameFocus(frame: SiteFrame) {
  bookViewStore.setFocusedPane(frame.paneId)
  refreshSiteFrameFocus()
}
const onFrameBlur = () => setTimeout(refreshSiteFrameFocus)
useEventListener(window, 'blur', () => setTimeout(() => {
  const frame = frameOf(document.activeElement)
  if (frame) onFrameFocus(frame)
}))
useEventListener(window, 'focus', () => setTimeout(refreshSiteFrameFocus))

// F5 / Ctrl+R while a site has focus (taken from the site by the host): reload the site, not the app.
useEventListener(window, 'keydown', (e: KeyboardEvent) => {
  const frame = frameOf(document.activeElement)
  if (!frame) return
  if (e.code === 'F5' || (e.ctrlKey && !e.altKey && e.code === 'KeyR')) {
    e.preventDefault()
    e.stopImmediatePropagation()
    reloadSiteFrame(frame)
  }
}, { capture: true })

// The reader typed in a site (siteFrameActivity script): protected from automatic unloading.
useEventListener(window, 'message', (e: MessageEvent) => {
  if (e.data?.type !== 'kitveiSiteInput') return
  for (const el of document.querySelectorAll<HTMLIFrameElement>('iframe.site-frame')) {
    if (el.contentWindow === e.source) {
      const frame = siteFrames.get(el.dataset.tabId ?? '')
      if (frame) frame.typed = true
    }
  }
})

// Split-view divider drags: an iframe under the pointer would swallow the drag.
const dragging = ref(false)
useEventListener(document, 'pointerdown', (e: PointerEvent) => {
  if ((e.target as Element | null)?.closest?.('[data-split-divider]')) dragging.value = true
}, { capture: true })
useEventListener(document, ['pointerup', 'pointercancel'], () => (dragging.value = false), { capture: true })
useEventListener(window, 'blur', () => (dragging.value = false))

// Host: where the reader went inside the site (the tab follows it), and new-window links.
onWebviewEvent((msg) => {
  if (msg.event === 'siteFrameNavigated' && typeof msg.url === 'string') {
    const frame = [...siteFrames.values()].find((f) => f.name === msg.frame)
    // A login page (OpenAthens) is not a site: the tab keeps the site's address.
    if (!frame || !websiteForUrl(msg.url) || msg.url === frame.currentUrl) return
    frame.currentUrl = msg.url
    frame.typed = false
    tabStore.updateTab(frame.tabId, { webUrl: msg.url })
  }
  if (msg.event === 'siteOpenInNewTab' && typeof msg.url === 'string') {
    const frame = frameOf(document.activeElement)
    if (!frame) return
    const site = websiteForUrl(msg.url)
    const tab = { route: '/web-view' as const, title: site ? websiteName(site) : displayHost(msg.url), webUrl: msg.url }
    if (frame.paneId === 2) tabStore.openPane2Tab(tab)
    else tabStore.openTab(tab)
  }
})

</script>

<template>
  <div class="site-frame-layer">
    <iframe
      v-for="frame in liveFrames"
      :key="`${frame.tabId}:${frame.startUrl}:${frame.reloadNonce}`"
      class="site-frame"
      :class="{ 'site-frame--dragging': dragging }"
      :data-tab-id="frame.tabId"
      :name="frame.name"
      :src="frameSrc(frame)"
      :style="frameStyle(frame)"
      allow="clipboard-read; clipboard-write; fullscreen"
      referrerpolicy="strict-origin-when-cross-origin"
      @load="frame.loading = false"
      @focus="onFrameFocus(frame)"
      @blur="onFrameBlur"
    />
    <SiteLeavePrompt />
  </div>
</template>

<style scoped>
.site-frame {
  position: fixed;
  box-sizing: border-box;
  /* Separates the site from the app's title bar and nav rail. */
  border: 1px solid var(--border-color);
  background: #fff;
  /* Above the page it covers, below menus, dialogs and the toast. */
  z-index: 1;
}
.site-frame--dragging {
  pointer-events: none;
}
</style>
