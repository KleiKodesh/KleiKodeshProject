<template>
    <div class="flex-column height-fill">
        <div class="page-header">
            <h2>חיפוש כזית</h2>
            <p>חיפוש מתקדם בספרי כזית</p>
        </div>

        <div class="flex-110 flex-center">
            <div class="placeholder-content">
                <Icon icon="fluent:search-28-filled"
                      class="large-icon" />
                <p>עמוד חיפוש כזית בפיתוח</p>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';
</script>

<style scoped>
.page-header {
    padding: 2rem;
    text-align: center;
    border-bottom: 1px solid var(--border-color);
}

.page-header h2 {
    margin: 0 0 0.5rem 0;
    color: var(--text-primary);
}

.page-header p {
    margin: 0;
    color: var(--text-secondary);
}

.placeholder-content {
    text-align: center;
    color: var(--text-secondary);
}

.large-icon {
    width: 64px;
    height: 64px;
    margin-bottom: 1rem;
    opacity: 0.5;
}
</style>