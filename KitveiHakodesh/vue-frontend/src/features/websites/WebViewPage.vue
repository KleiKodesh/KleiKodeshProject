<script setup lang="ts">
import { computed, inject, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'
import { refreshSiteFrameFocus } from './siteFrameFocus'
import { onWebviewEvent } from '@/webview-host/seforimDb'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useTabStore } from '@/stores/tabStore'
import MessageBanner from '@/components/MessageBanner.vue'
import { t } from '@/i18n'
import { displayHost, loadWebsites, websiteForUrl, websiteName, websitesRef } from './websitesList'

/**
 * One whitelisted Torah website, shown in an iframe. Hosted, the host lets these sites load
 * in a frame even when they send X-Frame-Options (WebSitesFrameHeaderStripper.cs).
 * An address that is not on the list is never loaded.
 */
const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()
const listLoaded = ref(false)
onMounted(() => void loadWebsites().then(() => (listLoaded.value = true)))

const webUrl = computed(() => paneNavigation.activeTab.webUrl ?? '')
const allowed = computed(() => !!webUrl.value && !!websiteForUrl(webUrl.value, websitesRef.value))
const reloadNonce = ref(0)
const tabId = paneNavigation.activeTabId
/** The iframe's name: how the host tells this tab where the user went inside the site. */
const frameName = `site-${tabId}`

// The address the iframe was LOADED with. The tab's webUrl follows the user's navigation inside
// the site (siteFrameNavigated below); that must not reload the frame, so only a webUrl that did
// not come from the frame itself (Back/Forward, history, a pasted address) loads it again.
const frameStartUrl = ref(webUrl.value)
let urlFromFrame = ''
watch(webUrl, (url) => {
  if (url && url !== urlFromFrame) frameStartUrl.value = url
  urlFromFrame = '' // consumed: Back then Forward to the same page must load it again
})
onBeforeUnmount(onWebviewEvent((msg) => {
  if (msg.event !== 'siteFrameNavigated' || msg.frame !== frameName || typeof msg.url !== 'string') return
  if (msg.url === webUrl.value) return
  // A login page the site sends you to (OpenAthens) is not a site: the tab keeps the site's address.
  if (!websiteForUrl(msg.url)) return
  urlFromFrame = msg.url
  tabStore.updateTab(tabId, { webUrl: msg.url })
}))
const frameRef = ref<HTMLIFrameElement | null>(null)

// Keyboard: while a site frame has focus the host takes the app's shortcut keys from it
// (AppViewerSiteShortcuts.cs; siteFrameFocus.ts). A click inside the frame never reaches the app
// document, so the frame's own focus event is also what makes this the focused split pane - the
// pane the replayed shortcuts act on.
const paneId = inject<1 | 2>('paneId', 1)
const bookViewStore = useBookViewStore()
function onFrameFocus() {
  bookViewStore.setFocusedPane(paneId)
  refreshSiteFrameFocus()
}
// Focus moving out (to the app or another frame): re-read once the move has landed.
const onFrameBlur = () => setTimeout(refreshSiteFrameFocus)
useEventListener(window, 'blur', () => setTimeout(() => {
  if (frameRef.value && document.activeElement === frameRef.value) onFrameFocus()
}))
useEventListener(window, 'focus', onFrameBlur)
onBeforeUnmount(() => void nextTick(refreshSiteFrameFocus))

// F5 / Ctrl+R (taken from the site while it has focus): reload the site, not the app.
const thisFrameHasFocus = () => !!frameRef.value && document.activeElement === frameRef.value
useEventListener(window, 'keydown', (e: KeyboardEvent) => {
  if (!thisFrameHasFocus()) return
  if (e.code === 'F5' || (e.ctrlKey && !e.altKey && e.code === 'KeyR')) {
    e.preventDefault()
    e.stopImmediatePropagation()
    reloadNonce.value++
  }
}, { capture: true })

// A new-window link inside the site: the host sends a whitelisted address here (anything else it
// opens in the system browser). It belongs to the frame the user was in - the focused one.
onBeforeUnmount(onWebviewEvent((msg) => {
  if (msg.event !== 'siteOpenInNewTab' || typeof msg.url !== 'string') return
  if (!thisFrameHasFocus()) return
  const site = websiteForUrl(msg.url)
  paneNavigation.openOrUpdateActiveTab({ route: '/web-view', title: site ? websiteName(site) : displayHost(msg.url), webUrl: msg.url }, true)
}))

/**
 * Hosted, the iframe loads the site itself (the host strips its frame block). The dev browser
 * cannot strip headers, so it loads the site through vite's dev proxy (websitesDevProxy.ts):
 * https://site/path -> http://site.localhost:<vite port + 1000>/path.
 */
const isHosted = typeof window.__webviewAction === 'function'
const frameSrc = computed(() => {
  if (isHosted || !import.meta.env.DEV) return frameStartUrl.value
  try {
    const target = new URL(frameStartUrl.value)
    return `http://${target.hostname}.localhost:${Number(location.port || 80) + 1000}${target.pathname}${target.search}${target.hash}`
  } catch {
    return frameStartUrl.value
  }
})
</script>

<template>
  <div class="web-view-page">
    <div v-if="!webUrl" class="web-view-message">{{ t('websites.noSite') }}</div>
    <template v-else-if="listLoaded">
      <iframe
        v-if="allowed"
        ref="frameRef"
        @focus="onFrameFocus"
        @blur="onFrameBlur"
        :key="`${frameStartUrl}:${reloadNonce}`"
        :name="frameName"
        :src="frameSrc"
        class="web-view-frame"
        allow="clipboard-read; clipboard-write; fullscreen"
        referrerpolicy="strict-origin-when-cross-origin"
      />
      <div v-else class="web-view-message">
        <MessageBanner :message="t('websites.notAllowedUrl', { url: webUrl })" :dismissible="false" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.web-view-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.web-view-frame {
  flex: 1;
  width: 100%;
  box-sizing: border-box;
  /* Separates the site from the app's title bar and nav rail. */
  border: 1px solid var(--border-color);
  background: #fff;
}
.web-view-message {
  margin: auto;
  padding: 16px;
  color: var(--text-secondary);
}
</style>
