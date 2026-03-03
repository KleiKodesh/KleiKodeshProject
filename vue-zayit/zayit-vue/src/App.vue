<template>
    <div class="height-fill app-container"
         tabindex="0"
         ref="appContainer">
        <TabControl class="height-fill" />
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useEventListener } from '@vueuse/core'
import TabControl from '@/components/workspace/TabControl.vue'
import { useTabs } from '@/components/workspace/useTabs'
import { useZoomHandler } from '@/components/shared/useZoom'

const { activeTab, closeTab, closeAllTabs } = useTabs()
const appContainer = ref<HTMLElement>()

// Zoom handling with keyboard, trackpad, and touch support
const currentZoom = computed({
    get: () => activeTab.value?.bookState?.zoom || 100,
    set: (value: number) => {
        const tab = activeTab.value
        if (tab?.bookState) {
            tab.bookState.zoom = value
        }
    }
})

const isBookViewPage = computed(() => activeTab.value?.currentPage === 'bookview')

useZoomHandler({
    zoom: currentZoom,
    target: appContainer,
    enabled: isBookViewPage
})

// Other keyboard shortcuts using useEventListener to support any keyboard layout
useEventListener('keydown', (event: KeyboardEvent) => {
    const hasCtrlOrMeta = event.ctrlKey || event.metaKey

    // Ctrl+W: Close current tab (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyW') {
        closeTab()
    }

    // Ctrl+X: Close all tabs (use event.code for keyboard layout independence)
    if (hasCtrlOrMeta && event.code === 'KeyX') {
        closeAllTabs()
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
