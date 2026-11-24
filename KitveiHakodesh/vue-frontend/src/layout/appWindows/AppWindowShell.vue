<script setup lang="ts">
/** The inside of an extra app window: the app shell for that window's tabs. */
import { useEventListener } from '@vueuse/core'
import AppShell from '@/layout/AppShell.vue'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { useTabStore } from '@/stores/tabStore'

const props = defineProps<{ windowKey: string }>()
const tabStore = useTabStore()

// This window got the focus: the reader works here now (shortcuts, new tabs).
useEventListener(useHostWindow(), 'focus', () => tabStore.focusWindow(props.windowKey))
</script>

<template>
  <div class="app-window-shell">
    <AppShell />
  </div>
</template>

<style scoped>
.app-window-shell {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
</style>
