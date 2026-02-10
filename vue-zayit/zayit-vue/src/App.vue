<template>
    <div class="height-fill app-container"
         tabindex="0"
         @keydown="handleKeydown"
         ref="appContainer">
        <TabControl class="height-fill" />
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import TabControl from './components/TabControl.vue';
import { useTabStore } from './stores/tabStore';

const tabStore = useTabStore();
const appContainer = ref<HTMLElement>();

const handleKeydown = (event: KeyboardEvent) => {
    // Ctrl+F is already prevented in main.ts
    // Components (BookLineViewer, BookCommentaryView) handle opening their search bars
    
    // Ctrl+W: Close current tab
    if (event.ctrlKey && event.key === 'w') {
        event.preventDefault();
        tabStore.closeTab();
        return;
    }

    // Ctrl+X: Close all tabs
    if (event.ctrlKey && event.key === 'x') {
        event.preventDefault();
        tabStore.closeAllTabs();
        return;
    }
};

onMounted(() => {
    // Focus the app container to ensure keyboard events are captured
    appContainer.value?.focus();
});
</script>

<style scoped>
.app-container {
    outline: none;
    /* Remove focus outline */
}
</style>
