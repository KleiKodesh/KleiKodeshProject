<template>
    <div class="loading-spinner-container">
        <div v-if="variant === 'spinner'"
             class="spinner-ring">
            <div class="ring"></div>
        </div>
        <div v-else-if="variant === 'dots'"
             class="spinner-dots">
            <div class="dot"></div>
            <div class="dot"></div>
            <div class="dot"></div>
        </div>
        <div v-else-if="variant === 'bars'"
             class="spinner-bars">
            <div class="bar"></div>
            <div class="bar"></div>
            <div class="bar"></div>
            <div class="bar"></div>
        </div>
        <div v-if="text"
             class="loading-text">{{ text }}</div>
    </div>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
    text?: string
    variant?: 'spinner' | 'dots' | 'bars'
    size?: 'small' | 'medium' | 'large'
}>(), {
    text: '',
    variant: 'dots',
    size: 'medium'
})
</script>

<style scoped>
.loading-spinner-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
}

/* Spinner Ring Animation */
.spinner-ring {
    display: flex;
    align-items: center;
    justify-content: center;
}

.ring {
    width: 40px;
    height: 40px;
    border: 3px solid var(--border-color, rgba(128, 128, 128, 0.2));
    border-top-color: var(--accent-color);
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
}

@keyframes spin {
    to {
        transform: rotate(360deg);
    }
}

/* Dots Animation */
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
    animation: bounce 1.4s ease-in-out infinite;
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

@keyframes bounce {
    0%, 80%, 100% {
        transform: translateY(0) scale(0.8);
        opacity: 0.5;
    }
    40% {
        transform: translateY(-12px) scale(1);
        opacity: 1;
    }
}

/* Bars Animation */
.spinner-bars {
    display: flex;
    gap: 4px;
    align-items: center;
    height: 32px;
}

.bar {
    width: 4px;
    height: 100%;
    background-color: var(--accent-color);
    border-radius: 2px;
    animation: stretch 1.2s ease-in-out infinite;
}

.bar:nth-child(1) {
    animation-delay: 0s;
}

.bar:nth-child(2) {
    animation-delay: 0.15s;
}

.bar:nth-child(3) {
    animation-delay: 0.3s;
}

.bar:nth-child(4) {
    animation-delay: 0.45s;
}

@keyframes stretch {
    0%, 40%, 100% {
        transform: scaleY(0.4);
        opacity: 0.5;
    }
    20% {
        transform: scaleY(1);
        opacity: 1;
    }
}

.loading-text {
    color: var(--text-secondary);
    font-size: 14px;
}
</style>
