<script setup lang="ts">
import { onMounted } from 'vue'
import WeeklyCalendarList from './WeeklyCalendarList.vue'
import { useZmanim } from './useZmanim'

const { activeCity, init: initZmanim } = useZmanim()

onMounted(() => initZmanim())
</script>

<template>
  <div class="cal-page">
    <WeeklyCalendarList :city="activeCity" />
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
