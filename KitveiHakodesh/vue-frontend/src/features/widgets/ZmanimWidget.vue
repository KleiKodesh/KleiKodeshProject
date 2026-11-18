<script setup lang="ts">
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import { cityDisplayName } from '@/features/hebrew-calendar/cityDisplayName'

const { rows, city } = useNextZman()
</script>

<template>
  <div class="zmanim-widget">
    <NextZmanPopup :rows="rows" :city-name="cityDisplayName(city)" />
  </div>
</template>

<style scoped>
.zmanim-widget { overflow-y: auto; }
/* The popup's own frame goes: the widget window is the frame now. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
</style>
