<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { getTheme } from '@/theme/themes'
import type { ThemePreset } from '@/theme/themeTypes'

const localFileStore = useLocalFileStore()
const settingsStore = useSettingsStore()
const tabStore = useTabStore()

const src = computed(() => localFileStore.virtualUrl)
const htmlMaskEnabled = computed(() => settingsStore.pdfPageFilters)

const iframeRef = ref<HTMLIFrameElement | null>(null)
const loaded = ref(false)
const error = ref<string | null>(null)
const loading = ref(false)
let loadTimeoutId: number | null = null

const tabId = tabStore.activeTabId
let scrollSaveTimer: number | null = null

// ── Load handling ─────────────────────────────────────────────────────────────

function clearLoadTimer() {
  if (loadTimeoutId !== null) {
    clearTimeout(loadTimeoutId)
    loadTimeoutId = null
  }
}

watch(src, (url) => {
  loaded.value = false
  error.value = null
  if (!url) {
    loading.value = false
    clearLoadTimer()
    return
  }
  loading.value = true
  clearLoadTimer()
  loadTimeoutId = window.setTimeout(() => {
    if (!loaded.value) {
      loading.value = false
      error.value = 'לא נטען — נסה רענון'
    }
  }, 8000)
})

function onIframeLoad() {
  loaded.value = true
  loading.value = false
  error.value = null
  clearLoadTimer()
  sendThemeToIframe()
  restoreScrollPosition()
}

async function retry() {
  error.value = null
  loading.value = true
  await localFileStore.restoreTab(tabId)
}

// ── Scroll persistence via postMessage ────────────────────────────────────────
// The local file iframe is cross-origin (kitvei-localfile-N vs kitvei-vue-app)
// so contentWindow.scrollY is inaccessible from Vue. Instead, a script injected
// by C# via AddScriptToExecuteOnDocumentCreatedAsync (JsBridge.IframeScrollScript)
// runs inside the iframe and posts { type: 'htmlViewScroll', scrollTop } messages
// to window.top. This component listens for those and persists the position via
// tabStore. On iframe load it posts a { type: 'htmlViewScrollTo' } command back
// into the iframe to restore the saved position.

function onWindowMessage(event: MessageEvent) {
  if (!event.data || event.data.type !== 'htmlViewScroll') return
  const scrollTop = event.data.scrollTop as number
  if (scrollSaveTimer !== null) clearTimeout(scrollSaveTimer)
  scrollSaveTimer = window.setTimeout(() => saveScrollPosition(scrollTop), 400)
}

async function saveScrollPosition(scrollTop: number) {
  scrollSaveTimer = null
  const existing = (await tabStore.getTabViewState(tabId)) ?? {}
  tabStore.setTabViewState(tabId, { ...existing, htmlViewScrollTop: scrollTop })
}

async function restoreScrollPosition() {
  const state = await tabStore.getTabViewState(tabId)
  if (!state?.htmlViewScrollTop) return
  await nextTick()
  iframeRef.value?.contentWindow?.postMessage(
    { type: 'htmlViewScrollTo', scrollTop: state.htmlViewScrollTop },
    '*',
  )
}

onMounted(() => {
  window.addEventListener('message', onWindowMessage)
  startThemeObserver()
  if (src.value) {
    loading.value = true
    clearLoadTimer()
    loadTimeoutId = window.setTimeout(() => {
      if (!loaded.value) {
        loading.value = false
        error.value = 'לא נטען — נסה רענון'
      }
    }, 8000)
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('message', onWindowMessage)
  themeObserver?.disconnect()
  clearLoadTimer()
  if (scrollSaveTimer !== null) {
    clearTimeout(scrollSaveTimer)
    scrollSaveTimer = null
  }
})

// ── Theme sync ────────────────────────────────────────────────────────────────
// Reads current theme colors from the app's root CSS custom properties and posts
// them into the iframe via postMessage. The injected IframeScrollScript applies
// them to the document body so txt (and html) files use the app's color scheme.

function getThemeColors(): Record<string, string> {
  const style = document.documentElement.style
  return {
    bgPrimary: style.getPropertyValue('--bg-primary-custom').trim(),
    textPrimary: style.getPropertyValue('--text-primary-custom').trim(),
    textSecondary: style.getPropertyValue('--text-secondary-custom').trim(),
  }
}

function sendThemeToIframe() {
  if (!iframeRef.value?.contentWindow) return
  iframeRef.value.contentWindow.postMessage(
    { type: 'htmlViewTheme', colors: getThemeColors() },
    '*',
  )
}

// Watch for theme changes (the data-theme-preset attribute changes on root when
// the user switches theme) and push the new colors into the iframe.
let themeObserver: MutationObserver | null = null

function startThemeObserver() {
  themeObserver?.disconnect()
  themeObserver = new MutationObserver(sendThemeToIframe)
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-theme-preset', 'style'],
  })
}

const htmlFilter = computed(() => {
  if (!htmlMaskEnabled.value) return 'none'
  const preset = document.documentElement.getAttribute('data-theme-preset') as ThemePreset | null
  const theme = preset ? getTheme(preset) : null
  if (!theme) return 'invert(0.85) hue-rotate(180deg) sepia(0.2)'
  return 'invert(0.85) hue-rotate(180deg) sepia(0.2) brightness(0.95) contrast(0.95)'
})
</script>

<template>
  <div class="html-view-page" :style="{ filter: htmlFilter }">
    <div v-if="!src" class="html-state-message">
      <span>אין קובץ פתוח</span>
    </div>
    <div v-else style="position: relative; flex: 1; min-height: 0; display: flex; flex-direction: column;">
      <iframe
        ref="iframeRef"
        :src="src"
        class="html-frame"
        @load="onIframeLoad"
      />
      <div v-if="loading" class="html-overlay">
        <span>טוען...</span>
      </div>
      <div v-if="error" class="html-overlay html-overlay--error">
        <span>{{ error }}</span>
        <button @click="retry">נסה שוב</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.html-view-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.html-frame {
  flex: 1;
  width: 100%;
  height: 100%;
  border: 0;
}
.html-state-message {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 14px;
}
.html-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  font-size: 14px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--bg-primary) 85%, transparent);
}
.html-overlay--error {
  color: var(--text-primary);
}
</style>
