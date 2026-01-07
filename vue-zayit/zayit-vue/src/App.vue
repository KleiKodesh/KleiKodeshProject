<template>
    <div 
        class="height-fill app-container" 
        tabindex="0" 
        @keydown="handleKeydown"
        ref="appContainer"
    >
        <TabControl class="height-fill" />
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import TabControl from './components/TabControl.vue';
import { useTabStore } from './stores/tabStore';

const tabStore = useTabStore();
const appContainer = ref<HTMLElement>();

const handleKeydown = (event: KeyboardEvent) => {
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

// Global keyboard event handler for when app doesn't have focus
const handleGlobalKeydown = (event: KeyboardEvent) => {
    // Only handle if no input/textarea is focused
    const activeElement = document.activeElement;
    if (activeElement && (
        activeElement.tagName === 'INPUT' || 
        activeElement.tagName === 'TEXTAREA' ||
        (activeElement as HTMLElement).contentEditable === 'true'
    )) {
        return;
    }
    
    handleKeydown(event);
};

onMounted(() => {
    // Focus the app container to ensure keyboard events are captured
    appContainer.value?.focus();
    
    // Add global keyboard listener
    document.addEventListener('keydown', handleGlobalKeydown);
});

onUnmounted(() => {
    document.removeEventListener('keydown', handleGlobalKeydown);
});
</script>

<style scoped>
.app-container {
    outline: none; /* Remove focus outline */
}
</style>
