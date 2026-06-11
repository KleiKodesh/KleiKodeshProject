<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { normalize } from '@/utils/normalizeText'
import {
  IconChevronDown20Regular,
  IconChevronUp20Regular,
  IconChevronRight20Regular,
  IconChevronLeft20Regular,
  IconMinimize20Regular,
  IconBookOpen20Regular,
  IconFilter20Regular,
} from '@iconify-prerendered/vue-fluent'
import type { CommentaryGroup } from './useCommentary'
import type { PinnedCommentaryGroup } from '../bookViewTypes'

const props = defineProps<{
  groups: CommentaryGroup[]
  scrollToGroup: (bookId: number, sectionLabel?: string, subSectionLabel?: string) => void
  activePinnedGroup: PinnedCommentaryGroup | null
  filterVisible?: boolean
  activeTocPath?: string
}>()

const emit = defineEmits<{
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'toggle-filter': []
  'toggle-search': []
  'open-book': [bookId: number, lineIndex: number]
  close: []
}>()

const filterBtnRef = ref<HTMLElement | null>(null)
const inputRef = ref<HTMLInputElement | null>(null)

defineExpose({ filterBtnRef })
const componentId = Math.random().toString(36).slice(2)

onMounted(() => nextTick(() => inputRef.value?.focus()))

const groupLabel = (g: CommentaryGroup) => g.path

function navigateToGroup(g: CommentaryGroup) {
  props.scrollToGroup(g.bookId, g.sectionLabel, g.subSectionLabel)
  if (inputRef.value) inputRef.value.value = ''
}

const activeGroup = computed(() =>
  props.activePinnedGroup
    ? props.groups.find(
        (g) =>
          g.bookId === props.activePinnedGroup!.bookId &&
          (g.sectionLabel ?? '') === props.activePinnedGroup!.sectionLabel &&
          (g.subSectionLabel ?? '') === props.activePinnedGroup!.subSectionLabel,
      ) ?? null
    : null,
)

const openBookTooltip = computed(() => {
  const bookTitle = activeGroup.value?.bookTitle ?? ''
  const full = props.activeTocPath ? `${bookTitle} ${props.activeTocPath}` : bookTitle
  return full ? `פתח ספר זה בלשונית חדשה\n${full}` : 'פתח ספר זה בלשונית חדשה'
})

const activeFullLabel = computed(() => {
  const bookTitle = activeGroup.value?.bookTitle ?? ''
  return props.activeTocPath ? `${bookTitle} ${props.activeTocPath}` : bookTitle
})

const activeIndex = computed(() => {
  if (!props.activePinnedGroup) return -1
  return props.groups.findIndex(
    (g) =>
      g.bookId === props.activePinnedGroup!.bookId &&
      (g.sectionLabel ?? '') === props.activePinnedGroup!.sectionLabel &&
      (g.subSectionLabel ?? '') === props.activePinnedGroup!.subSectionLabel,
  )
})
const hasPrevious = computed(() => activeIndex.value > 0)
const hasNext = computed(
  () => activeIndex.value !== -1 && activeIndex.value < props.groups.length - 1,
)

function openActiveBook() {
  const group = activeGroup.value
  if (group?.lines[0] != null) emit('open-book', group.bookId, group.lines[0].lineIndex)
}

function handleSelect() {
  // Only navigate if the user actually typed something — spurious 'change' events
  // fire when the datalist re-renders (e.g. during virtualizer scroll). Those events
  // have no preceding 'input' event so userHasTyped stays false.
  if (!userHasTyped) return
  userHasTyped.value = false
  const val = normalize(inputRef.value?.value ?? '')
  const match = props.groups.find(
    (g) => normalize(groupLabel(g)) === val || normalize(g.bookTitle) === val,
  )
  if (match) navigateToGroup(match)
}

// Set to true only when the user types — guards handleSelect against spurious
// 'change' events fired by datalist re-renders during virtualizer scrolls.
const userHasTyped = ref(false)

function handleKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.code === 'KeyF') {
    // Let the event bubble up to the commentary scroller's useScopedKeys handler
    // which will emit toggle-search correctly.
    return
  }
  if (e.key !== 'Enter') return
  const val = normalize((inputRef.value?.value ?? '').trim())
  if (!val) return
  const matches = props.groups.filter(
    (g) => normalize(groupLabel(g)).includes(val) || normalize(g.bookTitle).includes(val),
  )
  if (matches.length === 1) navigateToGroup(matches[0]!)
}
</script>

<template>
  <div class="nav">
    <button
      ref="filterBtnRef"
      class="btn c-pointer hover-bg"
      :class="{ active: filterVisible }"
      title="עץ מפרשים"
      @click.stop="emit('toggle-filter')"
    >
      <IconFilter20Regular />
    </button>
    <div class="sep" />
    <div class="search-wrapper" :title="activeFullLabel || undefined">
      <IconChevronDown20Regular class="search-icon" />
      <input
        ref="inputRef"
        type="text"
        name="commentary-search"
        class="search-input"
        :list="`commentary-list-${componentId}`"
        :placeholder="activeFullLabel || 'חפש מפרש...'"
        @input="userHasTyped = true"
        @change="handleSelect"
        @keydown="handleKeydown"
      />
      <datalist :id="`commentary-list-${componentId}`">
        <option v-for="g in groups" :key="`${g.bookId}::${g.sectionLabel ?? ''}::${g.subSectionLabel ?? ''}`" :value="groupLabel(g)" />
      </datalist>
    </div>
    <button
      class="btn c-pointer hover-bg"
      :disabled="!hasPrevious"
      title="מפרש קודם"
      @click="navigateToGroup(groups[activeIndex - 1]!)"
    >
      <IconChevronUp20Regular />
    </button>
    <button
      class="btn c-pointer hover-bg"
      :disabled="!hasNext"
      title="מפרש הבא"
      @click="navigateToGroup(groups[activeIndex + 1]!)"
    >
      <IconChevronDown20Regular />
    </button>
    <div class="sep" />
    <button
      class="btn c-pointer hover-bg"
      title="קטע קודם"
      @click="emit('navigate-section', 'prev', activePinnedGroup?.bookId ?? 0)"
    >
      <IconChevronRight20Regular />
    </button>
    <button
      class="btn c-pointer hover-bg"
      title="קטע הבא"
      @click="emit('navigate-section', 'next', activePinnedGroup?.bookId ?? 0)"
    >
      <IconChevronLeft20Regular />
    </button>
    <div class="sep" />
    <button
      class="btn c-pointer hover-bg"
      :title="openBookTooltip"
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
.btn.active {
  color: var(--accent-color);
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
  left: 2px;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
  pointer-events: none;
}
.search-input {
  width: 100%;
  height: 20px;
  padding-inline: 6px 0;
  border: 1px solid var(--border-color);
  border-radius: 999px;
  background: transparent;
  color: var(--text-primary);
  font-size: 11px;
  outline: none;
  appearance: none;
  -webkit-appearance: none;
}
.search-input::placeholder {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
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
