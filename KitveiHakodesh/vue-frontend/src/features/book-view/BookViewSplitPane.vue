<script setup lang="ts">
import SplitPane from '@/components/SplitPane.vue'
defineProps<{ bottomVisible: boolean }>()
</script>
<template>
  <SplitPane :bottom-visible="bottomVisible">
    <template #top><slot name="top" /></template>
    <template #bottom><slot name="bottom" /></template>
  </SplitPane>
</template>
