<script setup lang="ts">
import CommentaryView from './CommentaryView.vue'
import type { CommentaryGroup } from './useCommentary'
defineProps<{ selectedLineId: number | null; groups: CommentaryGroup[]; loading: boolean }>()
defineEmits<{ close: [] }>()
</script>
<template>
  <CommentaryView :selected-line-id="selectedLineId" :groups="groups" :loading="loading" @close="$emit('close')" />
</template>
