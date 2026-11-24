<script setup lang="ts">
/**
 * The extra app windows this page draws (the VS Code model): one page, one renderer,
 * many windows. Each is a popup of this page (DetachedWindow) that the host turns into a
 * full app window with its own tab strip (AppViewerPopup.cs), and shows its own tabs
 * (tab.windowKey). A tab moves between these windows live - nothing reloads.
 */
import { useEventListener } from '@vueuse/core'
import { useTabStore } from '@/stores/tabStore'
import AppWindow from './AppWindow.vue'

const tabStore = useTabStore()

// The main window got the focus: the reader works there now.
useEventListener(window, 'focus', () => tabStore.focusWindow(''))
</script>

<template>
  <AppWindow
    v-for="key in tabStore.extraWindowKeys"
    :key="key"
    :window-key="key"
  />
</template>
