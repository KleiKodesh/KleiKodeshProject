<template>
    <Icon icon="fluent:spinner-ios-20-regular"
          class="loading-spinner"
          :style="{ fontSize: `${size}px` }" />
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';

interface Props {
    size?: number
}

withDefaults(defineProps<Props>(), {
    size: 24
})
</script>

<style scoped>
.loading-spinner {
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