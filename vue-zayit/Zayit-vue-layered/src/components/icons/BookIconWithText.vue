<template>
    <div class="flex-column flex-center icon-with-text">
        <BookIcon class="book-icon" />
        <div v-if="text"
             class="text">{{ text }}</div>
    </div>
</template>

<script setup lang="ts">
import BookIcon from './BookIcon.vue';

withDefaults(defineProps<{
    text?: string
}>(), {
    text: ''
});
</script>

<style scoped>
.icon-with-text {
    gap: 16px;
    height: 100%;
    width: 100%;
    font-weight: 600;
}

.book-icon {
    width: 3rem;
    height: 3rem;
    color: var(--accent-color);
}
</style>
