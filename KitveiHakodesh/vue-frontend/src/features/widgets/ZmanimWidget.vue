<script setup lang="ts">
import NextZmanPopup from '@/features/home/NextZmanPopup.vue'
import { useNextZman } from '@/features/home/useNextZman'
import PlaceTreePicker from '@/features/hebrew-calendar/PlaceTreePicker.vue'

const { rows, city, setCity } = useNextZman()
</script>

<template>
  <div class="zmanim-widget">
    <PlaceTreePicker class="zw-place" :place-id="city?.id" @pick="setCity" />
    <NextZmanPopup :rows="rows" :city-name="''" />
  </div>
</template>

<style scoped>
.zw-place { margin: 8px 8px 0; }
/* The popup's own frame goes: the widget window (or the calendar's side column) is the frame. */
.zmanim-widget :deep(.zman-popup) { border: none; box-shadow: none; border-radius: 0; max-height: none; }
.zmanim-widget :deep(.zp-city) { display: none; }
</style>
