<template>
    <div class="flex-center height-fill">
        <div class="flex-column flex-center placeholder">
            <Icon icon="fluent:book-20-regular" class="placeholder-icon" />
            <span>בחר ספר עברי לצפייה</span>
        </div>
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue'
// TODO: Wire up to generic PDF viewer component
</script>

<style scoped>
.placeholder {
    gap: 12px;
    color: var(--text-secondary);
    font-size: 16px;
}

.placeholder-icon {
    font-size: 48px;
    opacity: 0.4;
}
</style>
