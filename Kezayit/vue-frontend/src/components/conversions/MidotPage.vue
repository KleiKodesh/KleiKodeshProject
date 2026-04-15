<script setup lang="ts">
import { ref, computed } from 'vue'
import { convert, toMetric, formatResult, explainConversion } from './midot'
import type { ConversionExplanation } from './midot'
import { LENGTH, AREA, VOLUME, WEIGHT, COINS, TIME } from './units'
import type { Unit, UnitSource, OpinionKey } from './units'
import { IconArrowSwap24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'

const settings = useSettingsStore()

// ── Source helpers ────────────────────────────────────────────────────────────
// Pick the single best source to display:
// 1. First source that has a URL (linkable)
// 2. Otherwise first source in the list

function normalizeSources(source: Unit['source']): UnitSource | null {
  if (!source) return null
  const list: UnitSource[] =
    typeof source === 'string' ? [{ label: source }] : Array.isArray(source) ? source : [source]
  return list.find((s) => s.url) ?? list[0] ?? null
}

const TABS = [
  { key: 'length', label: 'אורך' },
  { key: 'area', label: 'שטח' },
  { key: 'volume', label: 'נפח' },
  { key: 'weight', label: 'משקל' },
  { key: 'coins', label: 'מטבעות' },
  { key: 'time', label: 'זמן' },
]

const OPINION_LABELS: Record<string, string> = {
  naeh: 'ר"ח נאה',
  margolin: 'הידורי המידות',
  aruchHashulchan: 'ערוה"ש',
  ravMoshe: 'ר"מ פיינשטיין',
  chazonIsh: 'חזו"א',
  midotHatoraMeduyakot: 'מידות התורה המדויקות',
}

const HAMICHLOL_SOURCE: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}

// Returns the source of the *conversion* between two units:
// - If crossing Talmudic ↔ modern: the opinion (e.g. "ר"ח נאה")
// - If both Talmudic: the unit's own source (the chain)
// - If both modern: SI
function conversionSource(
  from: string,
  to: string,
  opinionKey: string,
  fromInfo: Unit | undefined,
): UnitSource | null {
  const MODERN_KEYS = new Set([
    'מ"מ',
    'ס"מ',
    "מ'",
    'ק"מ',
    "אינץ'",
    'רגל',
    'יארד',
    'מייל',
    'מ"מ²',
    'ס"מ²',
    "מ'²",
    'דונם',
    'הקטאר',
    "אינץ'²",
    'רגל²',
    'יארד²',
    'אקר',
    'מ"ל',
    "ל'",
    'כפית',
    'כף',
    'fl oz',
    'כוס',
    'פינט',
    'קווארט',
    'גלון',
    'UK fl oz',
    'UK פינט',
    'UK קווארט',
    'UK גאלון',
    'גרם',
    'ק"ג',
    'טון',
    'אונס',
    'ליברה',
    'סטון',
    'אונקיה טרוי',
    'שנייה',
    'דקה',
    'שעה (מודרנית)',
    'יום (מודרני)',
    'שבוע_מ',
  ])
  const fromModern = MODERN_KEYS.has(from)
  const toModern = MODERN_KEYS.has(to)

  if (fromModern && toModern) return { label: 'SI' }
  if (fromModern !== toModern) {
    // crossing Talmudic ↔ modern — source is the opinion
    return { label: OPINION_LABELS[opinionKey] ?? opinionKey }
  }
  // both Talmudic — source is the unit chain
  return normalizeSources(fromInfo?.source) ?? HAMICHLOL_SOURCE
}

const OPINION_TABS: { key: OpinionKey; label: string }[] = [
  { key: 'naeh', label: 'ר"ח נאה' },
  { key: 'margolin', label: 'דרהם קטן' },
  { key: 'aruchHashulchan', label: 'ערוה"ש' },
  { key: 'ravMoshe', label: 'ר"מ פיינשטיין' },
  { key: 'chazonIsh', label: 'חזו"א' },
  { key: 'midotHatoraMeduyakot', label: 'מידות המדויקות' },
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
const useRounded = ref(false)
const fromUnit = ref(DEFAULTS['length']!.from)
const toUnit = ref(DEFAULTS['length']!.to)
const inputValue = ref('1')

const units = computed(() => Object.keys(UNIT_MAPS[activeSystem.value]!))
const hasMetric = computed(() => HAS_METRIC.has(activeSystem.value))

function getUnit(name: string): Unit | undefined {
  const map = UNIT_MAPS[activeSystem.value] as Record<string, Unit>
  return map?.[name]
}

const fromUnitInfo = computed(() => getUnit(fromUnit.value))
const toUnitInfo = computed(() => getUnit(toUnit.value))

// Single source for the whole conversion — shown once at the swap row
const convSource = computed((): UnitSource | null => {
  const src = conversionSource(fromUnit.value, toUnit.value, opinion.value, fromUnitInfo.value)
  if (src?.label === 'SI') return null // modern↔modern: pure math, no source needed
  return src
})

const explanation = computed((): ConversionExplanation | null => {
  if (!inputValue.value || isNaN(parseFloat(inputValue.value))) return null
  try {
    return explainConversion(fromUnit.value, toUnit.value, opinion.value, useRounded.value)
  } catch {
    return null
  }
})

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
    return formatResult(convert(n, fromUnit.value, toUnit.value, opinion.value, useRounded.value))
  } catch {
    return ''
  }
})

const fromMetric = computed(() => {
  const n = parseFloat(inputValue.value)
  if (!inputValue.value || isNaN(n) || !hasMetric.value) return null
  try {
    const m = toMetric(n, fromUnit.value, opinion.value, useRounded.value)
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
    const converted = convert(n, fromUnit.value, toUnit.value, opinion.value, useRounded.value)
    const m = toMetric(converted, toUnit.value, opinion.value, useRounded.value)
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
        <button
          v-if="hasMetric"
          class="rounded-toggle"
          :class="{ active: useRounded }"
          :title="useRounded ? 'ערכים מעוגלים (כספרים)' : 'ערכים מדויקים'"
          @click="useRounded = !useRounded"
        >
          {{ useRounded ? 'מעוגל' : 'מדויק' }}
        </button>
      </div>
    </div>

    <!-- First-use disclaimer — shown instead of converter -->
    <div v-if="!settings.midotDisclaimerAccepted" class="disclaimer-wrap">
      <div class="disclaimer-box">
        <p class="disclaimer-title">שימו לב</p>
        <p class="disclaimer-body">
          המידות והשיעורים המוצגים כאן מבוססים על מקורות שונים ועל שיטות פוסקים שונות. ייתכנו
          אי-דיוקים, מחלוקות, ופרשנויות שונות בין הפוסקים.
        </p>
        <p class="disclaimer-body">השימוש בכלי זה הוא באחריות המשתמש בלבד.</p>
        <p class="disclaimer-body">לכל שאלה הלכתית יש לפנות לרב מוסמך.</p>
        <button class="disclaimer-btn" @click="settings.acceptMidotDisclaimer()">הבנתי, המשך</button>
      </div>
    </div>

    <div v-else class="converter">
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
        <div v-if="fromUnitInfo?.disputed" class="unit-disputed">{{ fromUnitInfo.disputed }}</div>
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
        <div v-if="toUnitInfo?.disputed" class="unit-disputed">{{ toUnitInfo.disputed }}</div>
      </div>

      <!-- Explanation — sits directly below the result -->
      <div v-if="explanation" class="explanation-bar">
        <span class="explanation-calc">{{ explanation.calc }}</span>
        <span class="explanation-sep"> · </span>
        <span class="explanation-source">
          <a
            v-if="explanation.source.url"
            :href="explanation.source.url"
            target="_blank"
            rel="noopener"
            class="source-link"
            >{{ explanation.source.label }}</a
          >
          <span v-else>{{ explanation.source.label }}</span>
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.midot-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow-y: auto;
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
  padding: 0 8px 0 24px;
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
  display: flex;
  flex-direction: column;
  padding: clamp(16px, 3vh, 32px) clamp(16px, 4vw, 32px);
  max-width: 480px;
  width: 100%;
  align-self: center;
  gap: clamp(4px, 1vh, 8px);
}

/* Opinion selector */
.opinion-select {
  height: 28px;
  padding: 0 8px 0 24px;
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

.rounded-toggle {
  height: 28px;
  padding: 0 8px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  font-size: 11px;
  font-family: inherit;
  cursor: pointer;
  flex-shrink: 0;
  white-space: nowrap;
}
.rounded-toggle.active {
  background: color-mix(in srgb, var(--accent-color) 15%, var(--bg-secondary));
  border-color: var(--accent-color);
  color: var(--accent-color);
}

/* Converter blocks */
.conv-block {
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
  padding: 0 10px 0 28px;
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
  display: block;
  width: 100%;
  height: 72px;
  padding: 0 14px;
  background: none;
  border: none;
  outline: none;
  font-size: clamp(24px, 4vh, 36px);
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
  height: 72px;
  padding: 0 14px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  font-size: clamp(24px, 4vh, 36px);
  color: var(--accent-color);
  font-weight: 600;
  direction: ltr;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.metric-hint {
  flex-shrink: 0;
  padding: 0 14px 4px;
  font-size: 11px;
  color: var(--text-secondary);
  text-align: left;
  direction: ltr;
}

.unit-source {
  display: none; /* removed — source now shown at swap row */
}

.conv-source {
  display: none;
}

.explanation-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  padding: 8px 2px 0;
  font-size: 11px;
  color: var(--text-secondary);
  direction: rtl;
}
.explanation-calc {
  font-weight: 600;
  color: var(--text-primary);
}
.explanation-sep {
  opacity: 0.4;
  flex-shrink: 0;
}
.explanation-source {
  white-space: nowrap;
}
.source-label {
  font-weight: 600;
}
.source-link {
  color: var(--accent-color);
  text-decoration: none;
  opacity: 0.85;
}
.source-link:hover {
  text-decoration: underline;
  opacity: 1;
}

.unit-disputed {
  flex-shrink: 0;
  padding: 0 14px 6px;
  font-size: 10px;
  color: color-mix(in srgb, var(--accent-color) 80%, var(--text-secondary));
  text-align: right;
  direction: rtl;
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

/* Disclaimer */
.disclaimer-wrap {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px clamp(16px, 4vw, 32px);
}
.disclaimer-box {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px;
  max-width: 360px;
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
  direction: rtl;
}
.disclaimer-title {
  font-size: 15px;
  font-weight: 700;
  color: var(--text-primary);
  margin: 0;
}
.disclaimer-body {
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  margin: 0;
}
.disclaimer-btn {
  margin-top: 4px;
  height: 36px;
  background: var(--accent-color);
  color: #fff;
  border: none;
  border-radius: 4px;
  font-size: 13px;
  font-family: inherit;
  font-weight: 600;
  cursor: pointer;
}
.disclaimer-btn:hover {
  opacity: 0.9;
}
</style>
