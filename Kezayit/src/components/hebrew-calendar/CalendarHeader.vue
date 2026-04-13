<script setup lang="ts">
import { ref, nextTick } from 'vue'
import {
  IconChevronLeft20Regular,
  IconChevronRight20Regular,
  IconHome20Regular,
  IconCalendarAgenda20Regular,
  IconCalendarMonth20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { HEB_MONTH_LIST, GREG_MONTH_LIST, hebYearToGematriya } from './useHebrewCalendar'

const props = defineProps<{
  viewMode: 'weekly' | 'monthly'
  hebLabel: string // e.g. "ניסן תשפ״ו" or "ניסן – אייר תשפ״ו"
  gregLabel: string // e.g. "אפריל 2026"
  displayMonth?: number
  displayYear?: number
  currentHebMonth?: number
  currentHebYear?: number
}>()

// Last token = Hebrew year gematriya, everything before = month (range)
function hebLabelMonth(label: string) {
  const parts = label.split(' ')
  return parts.slice(0, -1).join(' ')
}
function hebLabelYear(label: string) {
  const parts = label.split(' ')
  return parts[parts.length - 1] ?? ''
}

// Last token = Gregorian year number, everything before = month (range)
function gregLabelMonth(label: string) {
  const parts = label.split(' ')
  return parts.slice(0, -1).join(' ')
}
function gregLabelYear(label: string) {
  const parts = label.split(' ')
  return parts[parts.length - 1] ?? ''
}

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

const TODAY_YEAR = new Date().getFullYear()
const GREG_YEARS = Array.from({ length: 200 }, (_, i) => TODAY_YEAR - 100 + i)
const HEB_YEARS = Array.from({ length: 200 }, (_, i) => TODAY_YEAR + 3760 - 100 + i)

// ── Hebrew month ──────────────────────────────────────────────────────────
const showHebMonthDrop = ref(false)
const hebMonthBtnRef = ref<HTMLElement | null>(null)
const hebMonthDropRef = ref<HTMLElement | null>(null)
useDropdownClose(
  hebMonthDropRef,
  (e) => {
    if (hebMonthBtnRef.value?.contains((e as MouseEvent).target as Node)) return
    showHebMonthDrop.value = false
  },
  { ignore: [hebMonthBtnRef] },
)

// ── Hebrew year ───────────────────────────────────────────────────────────
const showHebYearDrop = ref(false)
const hebYearBtnRef = ref<HTMLElement | null>(null)
const hebYearDropRef = ref<HTMLElement | null>(null)
const hebYearListRef = ref<HTMLElement | null>(null)
useDropdownClose(
  hebYearDropRef,
  (e) => {
    if (hebYearBtnRef.value?.contains((e as MouseEvent).target as Node)) return
    showHebYearDrop.value = false
  },
  { ignore: [hebYearBtnRef] },
)
function openHebYearDrop() {
  showHebYearDrop.value = true
  nextTick(() => {
    const list = hebYearListRef.value
    const active = list?.querySelector<HTMLElement>('.active')
    if (!list || !active) return
    active.scrollIntoView({ block: 'nearest' })
    list.scrollTop +=
      active.offsetTop - list.scrollTop - list.clientHeight / 2 + active.offsetHeight / 2
  })
}

// ── Gregorian month ───────────────────────────────────────────────────────
const showGregMonthDrop = ref(false)
const gregMonthBtnRef = ref<HTMLElement | null>(null)
const gregMonthDropRef = ref<HTMLElement | null>(null)
useDropdownClose(
  gregMonthDropRef,
  (e) => {
    if (gregMonthBtnRef.value?.contains((e as MouseEvent).target as Node)) return
    showGregMonthDrop.value = false
  },
  { ignore: [gregMonthBtnRef] },
)

// ── Gregorian year ────────────────────────────────────────────────────────
const showGregYearDrop = ref(false)
const gregYearBtnRef = ref<HTMLElement | null>(null)
const gregYearDropRef = ref<HTMLElement | null>(null)
const gregYearListRef = ref<HTMLElement | null>(null)
useDropdownClose(
  gregYearDropRef,
  (e) => {
    if (gregYearBtnRef.value?.contains((e as MouseEvent).target as Node)) return
    showGregYearDrop.value = false
  },
  { ignore: [gregYearBtnRef] },
)
function openGregYearDrop() {
  showGregYearDrop.value = true
  nextTick(() => {
    const list = gregYearListRef.value
    const active = list?.querySelector<HTMLElement>('.active')
    if (!list || !active) return
    active.scrollIntoView({ block: 'nearest' })
    list.scrollTop +=
      active.offsetTop - list.scrollTop - list.clientHeight / 2 + active.offsetHeight / 2
  })
}

function selectHebMonth(m: number) {
  emit('select-heb-month', m)
  showHebMonthDrop.value = false
}
function selectHebYear(y: number) {
  emit('select-heb-year', y)
  showHebYearDrop.value = false
}
function selectGregMonth(m: number) {
  emit('select-greg-month', m)
  showGregMonthDrop.value = false
}
function selectGregYear(y: number) {
  emit('select-greg-year', y)
  showGregYearDrop.value = false
}
</script>

<template>
  <div class="cal-header">
    <!-- Physical RIGHT: Hebrew label -->
    <div class="header-he">
      <div class="picker-wrap">
        <span
          ref="hebMonthBtnRef"
          class="header-label-btn"
          :title="hebLabelMonth(hebLabel)"
          @click="showHebMonthDrop = !showHebMonthDrop"
        >
          {{ hebLabelMonth(hebLabel) }}
        </span>
        <div v-if="showHebMonthDrop" ref="hebMonthDropRef" class="drop-panel month-drop">
          <button
            v-for="m in HEB_MONTH_LIST"
            :key="m.num"
            class="drop-item"
            :class="{ active: currentHebMonth === m.num }"
            @click="selectHebMonth(m.num)"
          >
            {{ m.name }}
          </button>
        </div>
      </div>
      <div class="picker-wrap">
        <span ref="hebYearBtnRef" class="header-label-btn" @click="openHebYearDrop">
          {{ hebLabelYear(hebLabel) }}
        </span>
        <div v-if="showHebYearDrop" ref="hebYearDropRef" class="drop-panel year-drop">
          <div ref="hebYearListRef" class="year-list">
            <button
              v-for="y in HEB_YEARS"
              :key="y"
              class="drop-item"
              :class="{ active: currentHebYear === y }"
              @click="selectHebYear(y)"
            >
              {{ hebYearToGematriya(y) }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Center: < next · > prev · sep · home · sep · weekly · monthly -->
    <div class="header-nav">
      <button class="nav-btn" @click="emit('next')" title="הבא">
        <IconChevronLeft20Regular />
      </button>
      <button class="nav-btn" @click="emit('prev')" title="הקודם">
        <IconChevronRight20Regular />
      </button>
      <div class="nav-sep" />
      <button class="nav-btn" @click="emit('today')" title="היום"><IconHome20Regular /></button>
      <div class="nav-sep" />
      <button
        class="nav-btn"
        :class="{ active: viewMode === 'weekly' }"
        title="תצוגה שבועית"
        @click="emit('set-view', 'weekly')"
      >
        <IconCalendarAgenda20Regular />
      </button>
      <button
        class="nav-btn"
        :class="{ active: viewMode === 'monthly' }"
        title="תצוגה חודשית"
        @click="emit('set-view', 'monthly')"
      >
        <IconCalendarMonth20Regular />
      </button>
    </div>

    <!-- Physical LEFT: Gregorian label -->
    <div class="header-greg">
      <div class="picker-wrap">
        <span
          ref="gregMonthBtnRef"
          class="header-label-btn"
          :title="gregLabelMonth(gregLabel)"
          @click="showGregMonthDrop = !showGregMonthDrop"
        >
          {{ gregLabelMonth(gregLabel) }}
        </span>
        <div
          v-if="showGregMonthDrop"
          ref="gregMonthDropRef"
          class="drop-panel month-drop greg-drop"
        >
          <button
            v-for="(name, i) in GREG_MONTH_LIST"
            :key="i"
            class="drop-item"
            :class="{ active: displayMonth === i }"
            @click="selectGregMonth(i)"
          >
            {{ name }}
          </button>
        </div>
      </div>
      <div class="picker-wrap">
        <span ref="gregYearBtnRef" class="header-label-btn" @click="openGregYearDrop">
          {{ gregLabelYear(gregLabel) }}
        </span>
        <div v-if="showGregYearDrop" ref="gregYearDropRef" class="drop-panel year-drop greg-drop">
          <div ref="gregYearListRef" class="year-list">
            <button
              v-for="y in GREG_YEARS"
              :key="y"
              class="drop-item"
              :class="{ active: displayYear === y }"
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
.cal-header {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 3px 6px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  min-width: 0;
}

/* Hebrew label — physical right, grows to fill available space */
.header-he {
  display: flex;
  align-items: center;
  gap: 1px;
  flex: 1 1 0;
  min-width: 0;
  justify-content: flex-start;
  overflow: hidden;
}

/* Gregorian label — physical left, grows to fill available space */
.header-greg {
  display: flex;
  align-items: center;
  gap: 1px;
  flex: 1 1 0;
  min-width: 0;
  justify-content: flex-end;
  overflow: hidden;
}

.header-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
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
  position: relative;
  overflow: visible;
}
.nav-btn.active::after {
  display: none;
}
.nav-sep {
  width: 1px;
  height: 14px;
  background: var(--border-color);
  margin: 0 2px;
}

/* ── Picker ──────────────────────────────────────────────────────────────── */
.picker-wrap {
  position: relative;
  min-width: 0;
  overflow: hidden;
}
.header-label-btn {
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
.header-label-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

/* ── Dropdowns ───────────────────────────────────────────────────────────── */
.drop-panel {
  position: absolute;
  top: calc(100% + 4px);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
  z-index: 100;
  overflow: hidden;
}
.header-he .drop-panel {
  right: 0;
}
.header-greg .drop-panel {
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
  align-items: stretch;
}
.drop-item {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 26px;
  min-height: 0;
  padding: 0;
  line-height: 1;
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
