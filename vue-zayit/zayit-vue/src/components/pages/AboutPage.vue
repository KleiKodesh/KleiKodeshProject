<template>
    <div class="flex-column width-fill height-fill overflow-hidden about-page">
        <iframe src="https://kleikodesh.github.io/"
                class="flex-110 width-fill about-iframe"
                frameborder="0"
                title="About"></iframe>
    </div>
</template>

<script setup lang="ts">
// About page with iframe
</script>

<style scoped>
.about-page {
    background: var(--bg-primary);
}

.about-iframe {
    border: none;
    background: white;
}
</style>
