<script setup lang="ts">
import CommentaryView from './CommentaryView.vue'
import type { CommentaryGroup } from './useCommentary'
defineProps<{
  selectedLineId: number | null
  groups: CommentaryGroup[]
  loading: boolean
  hiddenBookIds: Set<number>
  searchQuery?: string
  currentMatchFlatIndex?: number
  currentMatchOccurrence?: number
  pinnedBookId?: number | null
}>()
defineEmits<{
  close: []
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'toggle-search': []
  scroll: [scrollIndex: number, scrollOffset: number]
  'update:hiddenBookIds': [value: Set<number>]
}>()
</script>
<template>
  <CommentaryView
    :selected-line-id="selectedLineId"
    :groups="groups"
    :loading="loading"
    :hidden-book-ids="hiddenBookIds"
    :search-query="searchQuery"
    :current-match-flat-index="currentMatchFlatIndex"
    :current-match-occurrence="currentMatchOccurrence"
    :pinned-book-id="pinnedBookId"
    @close="$emit('close')"
    @navigate-section="(d, b) => $emit('navigate-section', d, b)"
    @toggle-search="$emit('toggle-search')"
    @scroll="(i, o) => $emit('scroll', i, o)"
    @update:hidden-book-ids="(v) => $emit('update:hiddenBookIds', v)"
  />
</template>
