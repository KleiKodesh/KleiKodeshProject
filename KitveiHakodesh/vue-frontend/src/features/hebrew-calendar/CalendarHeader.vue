<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { HDate } from '@hebcal/core'
import {
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconHome20Regular,
  IconCalendarAgenda20Regular,
  IconCalendarMonth20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { HEB_MONTHS, GREG_MONTHS } from './useMonthlyView'

const props = defineProps<{
  viewMode: 'weekly' | 'monthly'
  hebrewLabel: string
  gregLabel: string
  hebMonth: number
  hebYear: number
  gregMonth: number
  gregYear: number
}>()

const emit = defineEmits<{
  (e: 'prev'): void
  (e: 'next'): void
  (e: 'today'): void
  (e: 'set-view', mode: 'weekly' | 'monthly'): void
  (e: 'select-heb-month', m: number): void
  (e: 'select-heb-year', y: number): void
  (e: 'select-greg-month', m: number): void
  (e: 'select-greg-year', y: number): void
}>()

// ── Label parsing ─────────────────────────────────────────────────────────────
function labelMonth(label: string) {
  return label.split(' ').slice(0, -1).join(' ')
}
function labelYear(label: string) {
  const p = label.split(' ')
  return p[p.length - 1] ?? ''
}

// ── Year lists ────────────────────────────────────────────────────────────────
const THIS_YEAR = new Date().getFullYear()
const GREG_YEARS = Array.from({ length: 200 }, (_, i) => THIS_YEAR - 100 + i)
const HEB_YEARS = Array.from({ length: 200 }, (_, i) => THIS_YEAR + 3760 - 100 + i)

function hebYearGem(y: number): string {
  try {
    return new HDate(1, 7, y).renderGematriya().split(' ').pop() ?? String(y)
  } catch {
    return String(y)
  }
}

// ── Dropdown helpers ──────────────────────────────────────────────────────────
function makeDropdown() {
  const show = ref(false)
  const btnRef = ref<HTMLElement | null>(null)
  const dropRef = ref<HTMLElement | null>(null)
  const listRef = ref<HTMLElement | null>(null)
  useDropdownClose(
    dropRef,
    (e) => {
      if (btnRef.value?.contains((e as MouseEvent).target as Node)) return
      show.value = false
    },
    { ignore: [btnRef] },
  )
  return { show, btnRef, dropRef, listRef }
}

const hebMonthDrop = makeDropdown()
const hebYearDrop = makeDropdown()
const gregMonthDrop = makeDropdown()
const gregYearDrop = makeDropdown()

function selectHebMonth(m: number) {
  emit('select-heb-month', m)
  hebMonthDrop.show.value = false
}
function selectHebYear(y: number) {
  emit('select-heb-year', y)
  hebYearDrop.show.value = false
}
function selectGregMonth(i: number) {
  emit('select-greg-month', i)
  gregMonthDrop.show.value = false
}
function selectGregYear(y: number) {
  emit('select-greg-year', y)
  gregYearDrop.show.value = false
}

function openYearDrop(drop: ReturnType<typeof makeDropdown>) {
  drop.show.value = !drop.show.value
  if (drop.show.value) {
    nextTick(() => {
      const list = drop.listRef.value
      const active = list?.querySelector<HTMLElement>('.active')
      if (!list || !active) return
      list.scrollTop = active.offsetTop - list.clientHeight / 2 + active.offsetHeight / 2
    })
  }
}
</script>

<template>
  <div class="header">
    <!-- Physical RIGHT: Hebrew label -->
    <div class="side side--he">
      <div class="picker">
        <span
          ref="hebMonthDrop.btnRef"
          class="label-btn"
          @click="hebMonthDrop.show.value = !hebMonthDrop.show.value"
        >
          {{ labelMonth(hebrewLabel) }}
        </span>
        <div v-if="hebMonthDrop.show.value" ref="hebMonthDrop.dropRef" class="drop month-drop">
          <button
            v-for="m in HEB_MONTHS"
            :key="m.num"
            class="drop-item"
            :class="{ active: hebMonth === m.num }"
            @click="selectHebMonth(m.num)"
          >
            {{ m.name }}
          </button>
        </div>
      </div>
      <div class="picker">
        <span ref="hebYearDrop.btnRef" class="label-btn" @click="openYearDrop(hebYearDrop)">
          {{ labelYear(hebrewLabel) }}
        </span>
        <div v-if="hebYearDrop.show.value" ref="hebYearDrop.dropRef" class="drop year-drop">
          <div ref="hebYearDrop.listRef" class="year-list">
            <button
              v-for="y in HEB_YEARS"
              :key="y"
              class="drop-item"
              :class="{ active: hebYear === y }"
              @click="selectHebYear(y)"
            >
              {{ hebYearGem(y) }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Center: navigation + view toggle -->
    <div class="nav">
      <button class="nav-btn" title="הבא" @click="emit('next')">
        <IconChevronLeft20Regular />
      </button>
      <button class="nav-btn" title="הקודם" @click="emit('prev')">
        <IconChevronRight20Regular />
      </button>
      <div class="sep" />
      <button class="nav-btn" title="היום" @click="emit('today')"><IconHome20Regular /></button>
      <div class="sep" />
      <button
        class="nav-btn"
        :class="{ active: viewMode === 'monthly' }"
        title="חודשי"
        @click="emit('set-view', 'monthly')"
      >
        <IconCalendarMonth20Regular />
      </button>
      <button
        class="nav-btn"
        :class="{ active: viewMode === 'weekly' }"
        title="שבועי"
        @click="emit('set-view', 'weekly')"
      >
        <IconCalendarAgenda20Regular />
      </button>
    </div>

    <!-- Physical LEFT: Gregorian label -->
    <div class="side side--greg">
      <div class="picker">
        <span
          ref="gregMonthDrop.btnRef"
          class="label-btn"
          @click="gregMonthDrop.show.value = !gregMonthDrop.show.value"
        >
          {{ labelMonth(gregLabel) }}
        </span>
        <div
          v-if="gregMonthDrop.show.value"
          ref="gregMonthDrop.dropRef"
          class="drop month-drop greg-drop"
        >
          <button
            v-for="(name, i) in GREG_MONTHS"
            :key="i"
            class="drop-item"
            :class="{ active: gregMonth === i }"
            @click="selectGregMonth(i)"
          >
            {{ name }}
          </button>
        </div>
      </div>
      <div class="picker">
        <span ref="gregYearDrop.btnRef" class="label-btn" @click="openYearDrop(gregYearDrop)">
          {{ labelYear(gregLabel) }}
        </span>
        <div
          v-if="gregYearDrop.show.value"
          ref="gregYearDrop.dropRef"
          class="drop year-drop greg-drop"
        >
          <div ref="gregYearDrop.listRef" class="year-list">
            <button
              v-for="y in GREG_YEARS"
              :key="y"
              class="drop-item"
              :class="{ active: gregYear === y }"
              @click="selectGregYear(y)"
            >
              {{ y }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.header {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 6px 12px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  overflow: visible;
  position: relative;
  z-index: 1;
}
.side {
  display: flex;
  align-items: center;
  gap: 1px;
  flex: 1;
  min-width: 0;
}
.side--he {
  justify-content: flex-start;
}
.side--greg {
  justify-content: flex-end;
}

.nav {
  display: flex;
  align-items: center;
  direction: ltr;
  flex-shrink: 0;
}
.nav-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 3px;
  color: var(--text-secondary);
}
.nav-btn.active {
  color: var(--accent-color, #0078d4);
}
.sep {
  width: 1px;
  height: 14px;
  background: var(--border-color);
  margin: 0 2px;
}

/* Picker */
.picker {
  position: relative;
  min-width: 0;
}
.label-btn {
  display: block;
  font-size: 11px;
  font-weight: 700;
  color: var(--text-primary);
  cursor: pointer;
  border-radius: 4px;
  padding: 2px 3px;
  user-select: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.label-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* Dropdowns */
.drop {
  position: absolute;
  top: calc(100% + 4px);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
  z-index: 10000;
  overflow: hidden;
}
.side--he .drop {
  right: 0;
}
.side--greg .drop {
  left: 0;
}

.month-drop {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  width: 162px;
  padding: 2px;
  gap: 1px;
}
.year-drop {
  width: 88px;
}
.year-list {
  height: 224px;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  padding: 2px;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.drop-item {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 26px;
  font-size: 12px;
  color: var(--text-primary);
  border-radius: 4px;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
}
.drop-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.drop-item.active {
  background: color-mix(in srgb, var(--accent-color, #0078d4) 15%, transparent);
  color: var(--accent-color, #0078d4);
  font-weight: 700;
}
</style>
