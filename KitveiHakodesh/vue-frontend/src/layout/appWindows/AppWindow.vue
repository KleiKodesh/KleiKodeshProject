<script setup lang="ts">
/** One extra app window: a popup of this page showing the shell for its own tabs. */
import { computed } from 'vue'
import DetachedWindow from '@/components/DetachedWindow.vue'
import { useTabStore } from '@/stores/tabStore'
import { catalogBookTitle } from '@/stores/catalogBookTitle'
import { APP_WINDOW_NAME_PREFIX, provideAppWindowKey } from '@/composables/appWindow'
import AppWindowShell from './AppWindowShell.vue'

const props = defineProps<{ windowKey: string }>()
provideAppWindowKey(props.windowKey)

const tabStore = useTabStore()
const title = computed(() => {
  const id = tabStore.activeIdIn(props.windowKey)
  const tab = tabStore.tabs.find((t) => t.id === id)
  return tab ? catalogBookTitle(tab) : ''
})
</script>

<template>
  <DetachedWindow
    :name="APP_WINDOW_NAME_PREFIX + windowKey"
    :title="title"
    :width="1000"
    :height="750"
    @closed="tabStore.closeWindow(windowKey)"
  >
    <AppWindowShell :window-key="windowKey" />
  </DetachedWindow>
</template>
