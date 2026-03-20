<script setup lang="ts">
import { ref } from 'vue'
import { IconDismiss20Regular, IconArrowStepOver20Regular } from '@iconify-prerendered/vue-fluent'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import CommentaryTypeDropdown from './CommentaryTypeDropdown.vue'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  bookTitle: string
  connectionTypes: string[]
  groups: CommentaryGroup[]
  scrollToGroup: (bookId: number) => void
  isSticky?: boolean
}>()
const emit = defineEmits<{ close: [] }>()

const CT_LABELS: Record<string, string> = {
  SOURCE: 'מקור', OTHER: 'אחר', COMMENTARY: 'מפרשים', TARGUM: 'תרגום', REFERENCE: 'הפניה',
}

const showNav = ref(false)
const dropdownOpen = ref(false)

function navigateToGroup(bookId: number) {
  props.scrollToGroup(bookId)
  dropdownOpen.value = false
}
</script>

<template>
  <div class="commentary-header" @click="isSticky && (showNav = true)">
    <template v-if="!showNav || !isSticky">
      <h5 class="book-title">{{ bookTitle }}</h5>
      <div v-if="isSticky" class="badge-wrapper" @click.stop="dropdownOpen = !dropdownOpen">
        <button class="badge" :class="{ active: dropdownOpen }">
          {{ connectionTypes[0] ? (CT_LABELS[connectionTypes[0]] ?? connectionTypes[0]) : '' }}
        </button>
        <CommentaryTypeDropdown v-if="dropdownOpen" :groups="groups" :ct-labels="CT_LABELS"
          @navigate="navigateToGroup" @close="dropdownOpen = false" />
      </div>
      <div v-if="isSticky" class="header-actions" @click.stop>
        <button class="action-btn c-pointer hover-bg" title="ניווט מפרשים" @click.stop="showNav = true">
          <IconArrowStepOver20Regular />
        </button>
        <button class="action-btn c-pointer hover-bg" title="סגור פרשנות" @click.stop="emit('close')">
          <IconDismiss20Regular />
        </button>
      </div>
    </template>
    <CommentaryHeaderNav v-if="showNav && isSticky" :groups="groups" :scroll-to-group="scrollToGroup"
      :book-title="bookTitle" @input-blur="showNav = false" />
  </div>
</template>

<style scoped>
.commentary-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-inline: 14px 6px;
  height: 32px;
  flex-shrink: 0;
  position: sticky;
  top: 0;
  z-index: 1;
  cursor: pointer;
  container-type: inline-size;
  background: var(--bg-primary);
}
.book-title {
  flex: 1;
  margin: 0;
  font-size: 13px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  user-select: text;
}
.book-title:hover { color: var(--accent-color); }
.badge-wrapper { position: relative; flex-shrink: 0; }
@container (max-width: 160px) { .badge-wrapper { display: none; } }
.badge {
  display: flex;
  align-items: center;
  font-size: 11px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 4px;
  padding: 2px 8px;
  white-space: nowrap;
}
.badge:hover, .badge.active {
  background: color-mix(in srgb, var(--accent-color) 15%, transparent);
  color: var(--accent-color);
}
.header-actions { display: flex; align-items: center; gap: 2px; flex-shrink: 0; }
.action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.action-btn svg { width: 14px; height: 14px; }
</style>
