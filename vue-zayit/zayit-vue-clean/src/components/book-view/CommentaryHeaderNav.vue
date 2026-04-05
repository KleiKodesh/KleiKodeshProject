<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import {
  IconChevronDown20Regular,
  IconChevronUp20Regular,
  IconChevronRight20Regular,
  IconChevronLeft20Regular,
  IconSearch20Regular,
  IconMinimize20Regular,
  IconBookOpen20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/common/IconTreeRtl.vue'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  groups: CommentaryGroup[]
  scrollToGroup: (bookId: number) => void
  bookTitle: string
  activeBookId: number
}>()

const emit = defineEmits<{
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'toggle-tree': []
  'toggle-search': []
  'open-book': [bookId: number, lineIndex: number]
  close: []
  'update:activeBookId': [bookId: number]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const componentId = Math.random().toString(36).slice(2)

onMounted(() => nextTick(() => inputRef.value?.focus()))

const CT_LABELS: Record<string, string> = {
  SOURCE: 'מקור',
  OTHER: 'קשרים',
  COMMENTARY: 'מפרשים',
  TARGUM: 'תרגום',
  REFERENCE: 'הפניה',
}

const groupLabel = (g: CommentaryGroup) => {
  const section =
    g.sectionLabel ?? CT_LABELS[g.connectionTypes[0] ?? ''] ?? g.connectionTypes[0] ?? ''
  return section ? `${section} > ${g.bookTitle}` : g.bookTitle
}

function navigateToGroup(bookId: number) {
  props.scrollToGroup(bookId)
  emit('update:activeBookId', bookId)
  if (inputRef.value) inputRef.value.value = ''
}

const activeIndex = computed(() => props.groups.findIndex((g) => g.bookId === props.activeBookId))
const hasPrevious = computed(() => activeIndex.value > 0)
const hasNext = computed(
  () => activeIndex.value !== -1 && activeIndex.value < props.groups.length - 1,
)

function openActiveBook() {
  const group = props.groups.find((g) => g.bookId === props.activeBookId)
  if (group?.lines[0] != null) emit('open-book', group.bookId, group.lines[0].lineIndex)
}

function handleSelect() {
  const val = inputRef.value?.value ?? ''
  const match = props.groups.find((g) => groupLabel(g) === val || g.bookTitle === val)
  if (match) navigateToGroup(match.bookId)
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key !== 'Enter') return
  const val = (inputRef.value?.value ?? '').trim()
  if (!val) return
  const matches = props.groups.filter(
    (g) => groupLabel(g).includes(val) || g.bookTitle.includes(val),
  )
  if (matches.length === 1) navigateToGroup(matches[0]!.bookId)
}
</script>

<template>
  <div class="nav">
    <button class="btn c-pointer hover-bg" title="הצג עץ מפרשים" @click.stop="emit('toggle-tree')">
      <IconTreeRtl />
    </button>
    <div class="sep" />
    <div class="search-wrapper">
      <IconChevronDown20Regular class="search-icon" />
      <input
        ref="inputRef"
        type="text"
        name="commentary-search"
        class="search-input"
        :list="`commentary-list-${componentId}`"
        :placeholder="bookTitle || 'חפש מפרש...'"
        @change="handleSelect"
        @keydown="handleKeydown"
      />
      <datalist :id="`commentary-list-${componentId}`">
        <option v-for="g in groups" :key="g.bookId" :value="groupLabel(g)" />
      </datalist>
    </div>
    <button
      class="btn c-pointer hover-bg"
      :disabled="!hasPrevious"
      title="מפרש קודם"
      @click="navigateToGroup(groups[activeIndex - 1]!.bookId)"
    >
      <IconChevronUp20Regular />
    </button>
    <button
      class="btn c-pointer hover-bg"
      :disabled="!hasNext"
      title="מפרש הבא"
      @click="navigateToGroup(groups[activeIndex + 1]!.bookId)"
    >
      <IconChevronDown20Regular />
    </button>
    <div class="sep" />
    <button
      class="btn c-pointer hover-bg"
      title="קטע קודם"
      @click="emit('navigate-section', 'prev', props.activeBookId)"
    >
      <IconChevronRight20Regular />
    </button>
    <button
      class="btn c-pointer hover-bg"
      title="קטע הבא"
      @click="emit('navigate-section', 'next', props.activeBookId)"
    >
      <IconChevronLeft20Regular />
    </button>
    <div class="sep" />
    <!-- <button
      class="btn c-pointer hover-bg"
      title="חיפוש במפרשים"
      @click.stop="emit('toggle-search')"
    >
      <IconSearch20Regular />
    </button> -->
    <button
      class="btn c-pointer hover-bg"
      title="פתח ספר זה בלשונית חדשה"
      @click.stop="openActiveBook()"
    >
      <IconBookOpen20Regular />
    </button>
    <button
      class="btn c-pointer hover-bg close-btn"
      title="סגור חלונית מפרשים"
      @click.stop="emit('close')"
    >
      <IconMinimize20Regular />
    </button>
  </div>
</template>

<style scoped>
.nav {
  display: flex;
  align-items: center;
  gap: 2px;
  width: 100%;
  height: 32px;
  overflow: hidden;
  background: var(--bg-primary);
  padding-inline: 6px;
}
.btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  color: var(--text-primary);
}
.btn svg {
  width: 14px;
  height: 14px;
}
.btn:disabled {
  opacity: 0.3;
  pointer-events: none;
}
.sep {
  width: 1px;
  height: 14px;
  flex-shrink: 0;
  background: color-mix(in srgb, var(--text-secondary) 20%, transparent);
  margin-inline: 2px;
}
.search-wrapper {
  flex: 1;
  min-width: 0;
  position: relative;
  display: flex;
  align-items: center;
}
.search-icon {
  position: absolute;
  left: 4px;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
  pointer-events: none;
}
.search-input {
  width: 100%;
  height: 20px;
  padding-inline: 6px 22px;
  border: 1px solid var(--border-color);
  border-radius: 999px;
  background: var(--input-bg);
  color: var(--text-primary);
  font-size: 11px;
  outline: none;
  appearance: none;
  -webkit-appearance: none;
}
.search-input::-webkit-calendar-picker-indicator,
.search-input::-webkit-list-button {
  display: none;
  opacity: 0;
  pointer-events: none;
}
.close-btn {
  margin-inline-start: auto;
}
</style>
