<template>
    <div v-if="progress > 0 && progress < 100"
         class="progress-bar"
         :style="{ width: progress + '%' }" />
</template>

<script setup lang="ts">
const props = defineProps<{
    progress: number // 0-100
}>()
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
