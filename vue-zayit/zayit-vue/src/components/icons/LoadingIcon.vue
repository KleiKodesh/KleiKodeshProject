<template>
    <div class="loading-icon">
        <Icon icon="fluent:spinner-ios-20-regular"
              class="loading-spinner" />
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';
</script>

<style scoped>
.loading-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 1rem;
}

.loading-spinner {
    font-size: 24px;
    color: var(--accent-color);
    animation: spin 1s linear infinite;
}

@keyframes spin {
    from {
        transform: rotate(0deg);
    }

    to {
        transform: rotate(360deg);
    }
}
</style>
