<template>
    <div class="loading-spinner-container">
        <div class="spinner-dots">
            <div class="dot"></div>
            <div class="dot"></div>
            <div class="dot"></div>
        </div>
        <div v-if="text"
             class="loading-text">{{ text }}</div>
    </div>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
    text?: string
}>(), {
    text: ''
})
</script>

<style scoped>
.loading-spinner-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
}

.spinner-dots {
    display: flex;
    gap: 8px;
    align-items: center;
}

.dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background-color: var(--accent-color);
    animation: pulse 1.4s ease-in-out infinite;
}

.dot:nth-child(1) {
    animation-delay: 0s;
}

.dot:nth-child(2) {
    animation-delay: 0.2s;
}

.dot:nth-child(3) {
    animation-delay: 0.4s;
}

@keyframes pulse {

    0%,
    80%,
    100% {
        opacity: 0.3;
        transform: scale(0.8);
    }

    40% {
        opacity: 1;
        transform: scale(1.2);
    }
}

.loading-text {
    color: var(--text-secondary);
    font-size: 14px;
}
</style>
