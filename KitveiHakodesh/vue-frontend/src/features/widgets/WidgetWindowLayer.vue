<script setup lang="ts">
import { storeToRefs } from 'pinia'
import WidgetWindow from './WidgetWindow.vue'
import { useWidgetWindowStore } from './widgetWindowStore'

const { windows } = storeToRefs(useWidgetWindowStore())
</script>

<template>
  <WidgetWindow v-for="win in windows" :key="win.id" :win="win" />
</template>
