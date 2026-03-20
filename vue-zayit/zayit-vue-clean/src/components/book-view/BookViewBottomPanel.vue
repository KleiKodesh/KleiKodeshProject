<script setup lang="ts">
import CommentaryView from './CommentaryView.vue'
defineProps<{ selectedLineId: number | null }>()
const emit = defineEmits<{ 'close': [] }>()
</script>

<template>
  <CommentaryView :selected-line-id="selectedLineId" @close="emit('close')" />
</template>
