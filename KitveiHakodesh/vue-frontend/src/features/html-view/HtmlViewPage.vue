<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
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

const loaded = ref(false)
const error = ref<string | null>(null)
const loading = ref(false)
let timeoutId: number | null = null

function clearTimer() {
  if (timeoutId !== null) {
    clearTimeout(timeoutId)
    timeoutId = null
  }
}

watch(src, (v) => {
  loaded.value = false
  error.value = null
  if (!v) {
    loading.value = false
    clearTimer()
    return
  }
  loading.value = true
  // If iframe hasn't loaded in 6s, show an error and let user retry
  clearTimer()
  timeoutId = window.setTimeout(() => {
    if (!loaded.value) {
      loading.value = false
      error.value = 'לא נטען — נסה רענון' // Hebrew: failed to load — try refresh
    }
  }, 6000)
})

onBeforeUnmount(() => clearTimer())

function onIframeLoad() {
  loaded.value = true
  loading.value = false
  error.value = null
  clearTimer()
}

async function retry() {
  error.value = null
  loading.value = true
  const tabId = tabStore.activeTabId
  await localFileStore.restoreTab(tabId)
}

// Helper function to convert hex to RGB object
function hexToRgbObj(hex: string): { r: number; g: number; b: number } {
  const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
  return result
    ? {
        r: parseInt(result[1], 16),
        g: parseInt(result[2], 16),
        b: parseInt(result[3], 16),
      }
    : { r: 0, g: 0, b: 0 }
}

// Compute theme-aware filter for HTML content
const htmlFilter = computed(() => {
  if (!htmlMaskEnabled.value) return 'none'
  
  const preset = document.documentElement.getAttribute('data-theme-preset') as ThemePreset | null
  const theme = preset ? getTheme(preset) : null
  if (!theme) return 'invert(0.95) hue-rotate(180deg)'
  
  // Use lighter filter logic for HTML content (less aggressive than PDF viewer)
  if (theme.isDark) {
    const { r, g, b } = hexToRgbObj(theme.reading.accentColor)
    const [rv, gv, bv] = [r / 255, g / 255, b / 255]
    const max = Math.max(rv, gv, bv),
      min = Math.min(rv, gv, bv),
      delta = max - min
    let hue = 0
    if (delta) {
      if (max === rv) hue = 60 * (((gv - bv) / delta) % 6)
      else if (max === gv) hue = 60 * ((bv - rv) / delta + 2)
      else hue = 60 * ((rv - gv) / delta + 4)
    }
    if (hue < 0) hue += 360
    const sat = max === 0 ? 0 : delta / max
    let f = 'invert(0.5) hue-rotate(180deg)'
    if (sat > 0.3)
      f += ` sepia(${Math.min(0.5, sat * 0.6)}) hue-rotate(${Math.round(hue)}deg) saturate(${Math.min(1.3, 1.1 + sat * 0.4)})`
    return f + ' brightness(0.9) contrast(0.95)'
  }

  const bg = hexToRgbObj(theme.reading.bgPrimary)
  const warmth = bg.r > bg.b && bg.g > bg.b ? (bg.r + bg.g - 2 * bg.b) / 255 : 0
  if (warmth > 0.2) {
    const s = Math.min(0.5, warmth)
    return `sepia(${s * 0.5}) brightness(${0.95 + (1 - s) * 0.02})`
  }
  const { r, g, b } = hexToRgbObj(theme.reading.accentColor)
  const [rv, gv, bv] = [r / 255, g / 255, b / 255]
  const max = Math.max(rv, gv, bv),
    min = Math.min(rv, gv, bv),
    delta = max - min
  const sat = max === 0 ? 0 : delta / max
  if (sat > 0.4) {
    let hue = 0
    if (delta) {
      if (max === rv) hue = 60 * (((gv - bv) / delta) % 6)
      else if (max === gv) hue = 60 * ((bv - rv) / delta + 2)
      else hue = 60 * ((rv - gv) / delta + 4)
    }
    if (hue < 0) hue += 360
    return `sepia(${Math.min(0.5, sat * 0.75)}) hue-rotate(${Math.round(hue)}deg) saturate(${Math.min(1.3, 1.1 + sat * 0.3)})`
  }
  return 'none'
})
</script>

<template>
  <div class="html-view-page" style="height:100%; display:flex; flex-direction:column;" :style="{ filter: htmlFilter }">
    <div v-if="!src" class="html-not-found" style="flex:1;display:flex;align-items:center;justify-content:center;">
      <div>אין קובץ פתוח</div>
    </div>
    <div v-else style="position:relative; flex:1;">
      <iframe
        :src="src"
        class="html-frame"
        style="width:100%; height:100%; border:0;"
        @load="onIframeLoad"
      />
      <div v-if="htmlMaskEnabled" class="html-mask" />
      <div v-if="loading" style="position:absolute; inset:0; display:flex; align-items:center; justify-content:center; background:rgba(255,255,255,0.6);">
        <div>טוען...</div>
      </div>
      <div v-if="error" style="position:absolute; inset:0; display:flex; align-items:center; justify-content:center; background:rgba(255,255,255,0.9); flex-direction:column; gap:8px;">
        <div>{{ error }}</div>
        <div>
          <button @click="retry">נסה שוב</button>
        </div>
      </div>
    </div>
  </div>
</template>
