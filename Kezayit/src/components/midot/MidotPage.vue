<script setup lang="ts">
import { ref, computed } from 'vue'
import { convert, toMetric, formatResult } from './midot'
import { LENGTH, VOLUME, WEIGHT, COINS, TIME } from './midotDefinitions'
import TabStrip from '@/components/common/TabStrip.vue'
import { IconArrowSwap24Regular } from '@iconify-prerendered/vue-fluent'
import type { MetricOpinion } from './midot'

const TABS = [
  { key: 'length', label: 'אורך' },
  { key: 'volume', label: 'נפח' },
  { key: 'weight', label: 'משקל' },
  { key: 'coins', label: 'מטבעות' },
  { key: 'time', label: 'זמן' },
]

const OPINION_TABS = [
  { key: 'naeh', label: 'ר׳ ח׳ נאה' },
  { key: 'chazonIsh', label: 'חזון איש' },
]

const DEFAULTS: Record<string, { from: string; to: string }> = {
  length: { from: 'אמה', to: 'ס"מ' },
  volume: { from: 'לוג', to: 'מ"ל' },
  weight: { from: 'שקל', to: 'גרם' },
  coins: { from: 'סלע', to: 'דינר' },
  time: { from: 'שעה', to: 'חלק' },
}

const UNIT_MAPS: Record<string, Record<string, unknown>> = {
  length: LENGTH,
  volume: VOLUME,
  weight: WEIGHT,
  coins: COINS,
  time: TIME,
}

const activeSystem = ref('length')
const opinion = ref<MetricOpinion>('naeh')
const fromUnit = ref('אמה')
const toUnit = ref('טפח')
const inputValue = ref('1')

const units = computed(() => Object.keys(UNIT_MAPS[activeSystem.value]!))

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
  if (!inputValue.value || isNaN(n)) return null
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
  if (!inputValue.value || isNaN(n)) return null
  try {
    const converted = convert(n, fromUnit.value, toUnit.value, opinion.value)
    const m = toMetric(converted, toUnit.value, opinion.value)
    if (!m) return null
    return `${formatResult(m.value)} ${m.metricUnit}`
  } catch {
    return null
  }
})

const hasMetric = computed(() => activeSystem.value !== 'time' && activeSystem.value !== 'coins')
</script>

<template>
  <div class="midot-page">
    <TabStrip v-model="activeSystem" :tabs="TABS" @update:model-value="onSystemChange" />

    <div class="converter">
      <!-- Opinion toggle — only for systems with metric -->
      <div v-if="hasMetric" class="opinion-row">
        <span class="opinion-label">שיטה:</span>
        <div class="opinion-toggle">
          <button
            v-for="op in OPINION_TABS"
            :key="op.key"
            :class="{ active: opinion === op.key }"
            @click="opinion = op.key as MetricOpinion"
          >
            {{ op.label }}
          </button>
        </div>
      </div>

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
        <div v-if="hasMetric && fromMetric" class="metric-hint">≈ {{ fromMetric }}</div>
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
        <div v-if="hasMetric && toMetricDisplay" class="metric-hint">≈ {{ toMetricDisplay }}</div>
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

.converter {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: clamp(8px, 2vh, 20px) 16px;
  max-width: 480px;
  width: 100%;
  align-self: center;
  min-height: 0;
  gap: clamp(4px, 1vh, 8px);
}

/* Opinion toggle */
.opinion-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}
.opinion-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.opinion-toggle {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
}
.opinion-toggle button {
  height: 26px;
  padding: 0 10px;
  font-size: 11px;
  background: var(--bg-secondary);
  border: none;
  border-radius: 0;
  color: var(--text-secondary);
}
.opinion-toggle button + button {
  border-right: 1px solid var(--border-color);
}
.opinion-toggle button.active {
  background: var(--accent-color);
  color: #fff;
}

/* Each from/to block */
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
