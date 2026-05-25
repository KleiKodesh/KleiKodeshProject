<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useTabStore } from '@/stores/tabStore'

const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
const src = computed(() => localFileStore.virtualUrl)

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
</script>

<template>
  <div class="addin-view-page" style="height:100%; display:flex; flex-direction:column;">
    <div v-if="!src" class="addin-not-found" style="flex:1;display:flex;align-items:center;justify-content:center;">
      <div>אין קובץ פתוח</div>
    </div>
    <div v-else style="position:relative; flex:1;">
      <iframe
        :src="src"
        class="addin-frame"
        style="width:100%; height:100%; border:0;"
        @load="onIframeLoad"
      />
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
