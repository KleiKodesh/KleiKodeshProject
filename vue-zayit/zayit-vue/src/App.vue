<template>
    <div class="height-fill app-container"
         tabindex="0"
         ref="appContainer">
        <TabControl class="height-fill" />
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import TabControl from './components/TabControl.vue'
import { useTabStore } from './stores/tabStore'

const tabStore = useTabStore()
const appContainer = ref<HTMLElement>()

// Keyboard shortcuts using useEventListener to support any keyboard layout
useEventListener('keydown', (event: KeyboardEvent) => {
    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    // Ctrl+W: Close current tab (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyW') {
        tabStore.closeTab()
    }

    // Ctrl+X: Close all tabs (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyX') {
        tabStore.closeAllTabs()
    }
})

onMounted(() => {
    // Focus the app container to ensure keyboard events are captured
    appContainer.value?.focus()
})
</script>

<style scoped>
.app-container {
    outline: none;
    /* Remove focus outline */
}
</style>
