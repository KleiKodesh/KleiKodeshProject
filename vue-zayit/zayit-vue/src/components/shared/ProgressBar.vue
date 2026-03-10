<template>
    <div v-if="visible"
         class="progress-bar"
         :style="{ width: progress + '%' }" />
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'

const props = defineProps<{
    progress: number // 0-100
}>()

const visible = ref(true)

// Hide progress bar only after it reaches 100% and animation completes
// Show again when a new loading cycle starts (progress resets to low value)
watch(() => props.progress, (newProgress, oldProgress) => {
    if (newProgress >= 100) {
        // Wait for CSS transition (300ms) before hiding
        setTimeout(() => {
            visible.value = false
        }, 300)
    } else if (newProgress < 20 && oldProgress !== undefined && oldProgress > newProgress) {
        // New loading cycle detected - show progress bar again
        visible.value = true
    }
})
</script>

<style scoped>
.progress-bar {
    position: absolute;
    top: 0;
    left: 0;
    height: 2px;
    background: var(--primary-color, #0078d4);
    transition: width 0.3s ease;
    z-index: 1000;
}
</style>
