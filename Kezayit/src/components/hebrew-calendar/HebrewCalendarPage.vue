<script setup lang="ts">
import { onMounted } from 'vue'
import WeeklyCalendarList from './WeeklyCalendarList.vue'
import { useZmanim } from './useZmanim'

const { activeCity, cities, setCity, init: initZmanim } = useZmanim()

onMounted(() => initZmanim())
</script>

<template>
  <div class="cal-page">
    <WeeklyCalendarList :city="activeCity" :cities="cities" @city-change="setCity" />
  </div>
</template>

<style scoped>
.cal-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  box-sizing: border-box;
}
</style>
