<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'

const currentTime = ref('')
const isFullscreen = ref(false)

function checkFullscreen() {
  isFullscreen.value = window.innerHeight >= screen.height
}

function updateTime() {
  const now = new Date()
  const hours = String(now.getHours()).padStart(2, '0')
  const minutes = String(now.getMinutes()).padStart(2, '0')
  currentTime.value = `${hours}:${minutes}`
}

let intervalId: ReturnType<typeof setInterval>

useEventListener(window, 'resize', checkFullscreen)

onMounted(() => {
  checkFullscreen()
  updateTime()
  intervalId = setInterval(updateTime, 10_000)
})

onBeforeUnmount(() => {
  clearInterval(intervalId)
})
</script>

<template>
  <div v-if="isFullscreen" class="clock-widget" aria-live="off" aria-label="שעון">
    {{ currentTime }}
  </div>
</template>

<style scoped>
.clock-widget {
  position: fixed;
  bottom: 5px;
  left: 16px;
  z-index: 200;
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--bg-primary) 60%, transparent);
  padding: 3px 8px;
  border-radius: 4px;
  pointer-events: none;
  user-select: none;
  letter-spacing: 0.04em;
}
</style>
