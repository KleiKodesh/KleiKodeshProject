<script setup lang="ts">
import { ref, computed } from 'vue'
import { convert, toMetric, formatResult } from './midot'
import { LENGTH, AREA, VOLUME, WEIGHT, COINS, TIME } from './units'
import { IconArrowSwap24Regular } from '@iconify-prerendered/vue-fluent'
import type { OpinionKey } from './units'

const TABS = [
  { key: 'length', label: 'אורך' },
  { key: 'area', label: 'שטח' },
  { key: 'volume', label: 'נפח' },
  { key: 'weight', label: 'משקל' },
  { key: 'coins', label: 'מטבעות' },
  { key: 'time', label: 'זמן' },
]

const OPINION_TABS: { key: OpinionKey; label: string }[] = [
  { key: 'naeh', label: 'ר׳ ח׳ נאה' },
  { key: 'aruchHashulchan', label: 'ערוה"ש' },
  { key: 'ravMoshe', label: 'ר׳ מ׳ פיינשטיין' },
  { key: 'chazonIsh', label: 'חזון איש' },
]

const DEFAULTS: Record<string, { from: string; to: string }> = {
  length: { from: 'אמה', to: 'ס"מ' },
  area: { from: 'אמה מרובעת', to: 'ס"מ²' },
  volume: { from: 'לוג', to: 'מ"ל' },
  weight: { from: 'שקל', to: 'גרם' },
  coins: { from: 'סלע', to: 'דינר' },
  time: { from: 'שעה', to: 'חלק' },
}

const UNIT_MAPS: Record<string, Record<string, unknown>> = {
  length: LENGTH,
  area: AREA,
  volume: VOLUME,
  weight: WEIGHT,
  coins: COINS,
  time: TIME,
}

// Systems that have metric hints and opinion selector
const HAS_METRIC = new Set(['length', 'area', 'volume', 'weight'])

const activeSystem = ref('length')
const opinion = ref<OpinionKey>('naeh')
const fromUnit = ref(DEFAULTS['length']!.from)
const toUnit = ref(DEFAULTS['length']!.to)
const inputValue = ref('1')

const units = computed(() => Object.keys(UNIT_MAPS[activeSystem.value]!))
const hasMetric = computed(() => HAS_METRIC.has(activeSystem.value))

function onSystemChange(key: string) {
  activeSystem.value = key
  fromUnit.value = DEFAULTS[key]!.from
  toUnit.value = DEFAULTS[key]!.to
  inputValue.value = '1'
}

function swap() {
  const tmp = fromUnit.value
  fromUnit.value = toUnit.value
  toUnit.value = tmp
}

const result = computed(() => {
  const n = parseFloat(inputValue.value)
  if (!inputValue.value || isNaN(n)) return ''
  try {
    return formatResult(convert(n, fromUnit.value, toUnit.value, opinion.value))
  } catch {
    return ''
  }
})

const fromMetric = computed(() => {
  const n = parseFloat(inputValue.value)
  if (!inputValue.value || isNaN(n) || !hasMetric.value) return null
  try {
    const m = toMetric(n, fromUnit.value, opinion.value)
    if (!m) return null
    return `${formatResult(m.value)} ${m.metricUnit}`
  } catch {
    return null
  }
})

const toMetricDisplay = computed(() => {
  const n = parseFloat(inputValue.value)
  if (!inputValue.value || isNaN(n) || !hasMetric.value) return null
  try {
    const converted = convert(n, fromUnit.value, toUnit.value, opinion.value)
    const m = toMetric(converted, toUnit.value, opinion.value)
    if (!m) return null
    return `${formatResult(m.value)} ${m.metricUnit}`
  } catch {
    return null
  }
})
</script>

<template>
  <div class="midot-page">
    <div class="top-bar">
      <div class="top-bar-inner">
        <select
          class="system-select"
          :value="activeSystem"
          @change="onSystemChange(($event.target as HTMLSelectElement).value)"
        >
          <option v-for="tab in TABS" :key="tab.key" :value="tab.key">{{ tab.label }}</option>
        </select>
        <select v-if="hasMetric" v-model="opinion" class="opinion-select">
          <option v-for="op in OPINION_TABS" :key="op.key" :value="op.key">{{ op.label }}</option>
        </select>
      </div>
    </div>

    <div class="converter">
      <!-- From block -->
      <div class="conv-block">
        <select v-model="fromUnit" class="unit-select">
          <option v-for="u in units" :key="u" :value="u">{{ u }}</option>
        </select>
        <input
          v-model="inputValue"
          class="value-input"
          type="number"
          inputmode="decimal"
          placeholder="0"
        />
        <div v-if="fromMetric" class="metric-hint">≈ {{ fromMetric }}</div>
      </div>

      <!-- Swap -->
      <div class="swap-row">
        <div class="swap-line" />
        <button class="swap-btn" @click="swap">
          <IconArrowSwap24Regular />
        </button>
        <div class="swap-line" />
      </div>

      <!-- To block -->
      <div class="conv-block">
        <select v-model="toUnit" class="unit-select">
          <option v-for="u in units" :key="u" :value="u">{{ u }}</option>
        </select>
        <div class="result-value">{{ result || '—' }}</div>
        <div v-if="toMetricDisplay" class="metric-hint">≈ {{ toMetricDisplay }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.midot-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  direction: rtl;
  background: var(--bg-primary);
}

/* Top bar — system + opinion selects side by side */
.top-bar {
  display: flex;
  justify-content: center;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.top-bar-inner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px clamp(16px, 4vw, 32px);
  max-width: 480px;
  width: 100%;
}

.system-select {
  flex: 1;
  height: 28px;
  padding: 0 8px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  font-size: 12px;
  font-family: inherit;
  direction: rtl;
  cursor: pointer;
  outline: none;
  font-weight: 600;
}
.system-select:focus {
  border-color: var(--accent-color);
}

.converter {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: clamp(16px, 3vh, 32px) clamp(16px, 4vw, 32px);
  max-width: 480px;
  width: 100%;
  align-self: center;
  min-height: 0;
  gap: clamp(4px, 1vh, 8px);
}

/* Opinion selector */
.opinion-select {
  height: 28px;
  padding: 0 8px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  font-size: 11px;
  font-family: inherit;
  direction: rtl;
  cursor: pointer;
  outline: none;
  flex-shrink: 0;
}
.opinion-select:focus {
  border-color: var(--accent-color);
}

/* Converter blocks */
.conv-block {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
}

.unit-select {
  flex-shrink: 0;
  display: block;
  width: 100%;
  height: 32px;
  padding: 0 10px;
  background: var(--bg-toolbar);
  border: none;
  border-bottom: 1px solid var(--border-color);
  color: var(--text-secondary);
  font-size: 12px;
  font-family: inherit;
  direction: rtl;
  cursor: pointer;
  outline: none;
}
.unit-select:focus {
  color: var(--text-primary);
}

.value-input {
  flex: 1;
  min-height: 0;
  display: block;
  width: 100%;
  padding: 0 14px;
  background: none;
  border: none;
  outline: none;
  font-size: clamp(18px, 3.5vh, 34px);
  font-family: inherit;
  color: var(--text-primary);
  text-align: right;
  direction: ltr;
  box-sizing: border-box;
}
.value-input::-webkit-outer-spin-button,
.value-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
}
.value-input[type='number'] {
  -moz-appearance: textfield;
}

.result-value {
  flex: 1;
  min-height: 0;
  padding: 0 14px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  font-size: clamp(18px, 3.5vh, 34px);
  color: var(--accent-color);
  font-weight: 600;
  direction: ltr;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.metric-hint {
  flex-shrink: 0;
  padding: 0 14px 6px;
  font-size: 11px;
  color: var(--text-secondary);
  text-align: left;
  direction: ltr;
}

/* Swap row */
.swap-row {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 10px;
}
.swap-line {
  flex: 1;
  height: 1px;
  background: var(--border-color);
}
.swap-btn {
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 50%;
  color: var(--text-secondary);
}
.swap-btn:hover {
  color: var(--text-primary);
  border-color: var(--text-secondary);
}
</style>
