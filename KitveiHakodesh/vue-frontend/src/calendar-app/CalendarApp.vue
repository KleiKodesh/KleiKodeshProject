<script setup lang="ts">
import HebrewCalendarPage from '@/features/hebrew-calendar/HebrewCalendarPage.vue'
</script>

<template>
  <HebrewCalendarPage extended class="calendar-app" />
</template>

<style>
html, body, #app { height: 100%; margin: 0; }
body { background: var(--bg-primary); color: var(--text-primary); }
</style>
<style scoped>
.calendar-app { height: 100%; }
</style>
