<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed } from 'vue'
import { convert, toMetric, formatResult, explainConversion } from './halachicUnits'
import type { ConversionExplanation } from './halachicUnits'
import { LENGTH, AREA, VOLUME, WEIGHT, COINS, TIME } from './units'
import type { Unit, UnitSource, OpinionKey } from './units'
import { IconArrowSwap24Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import AppSelect from '@/components/AppSelect.vue'
import PageCard from '@/components/PageCard.vue'

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
  { key: 'length', label: t('halachicUnits.tabs.length') },
  { key: 'area', label: t('halachicUnits.tabs.area') },
  { key: 'volume', label: t('halachicUnits.tabs.volume') },
  { key: 'weight', label: t('halachicUnits.tabs.weight') },
  { key: 'coins', label: t('halachicUnits.tabs.coins') },
  { key: 'time', label: t('halachicUnits.tabs.time') },
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
const unitOptions = computed(() => units.value.map((u) => ({ value: u, label: u })))
const opinionOptions = OPINION_TABS.map((o) => ({ value: o.key, label: o.label }))
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
    <PageCard>
      <template #header>
        <div class="systems">
          <div class="systems-grid" role="tablist">
            <button
              v-for="tab in TABS"
              :key="tab.key"
              class="system-chip"
              :class="{ active: tab.key === activeSystem }"
              role="tab"
              @click="onSystemChange(tab.key)"
            >
              {{ tab.label }}
            </button>
          </div>
        </div>
      </template>

      <!-- First-use disclaimer — shown instead of converter -->
      <div v-if="!settings.midotDisclaimerAccepted" class="disclaimer-wrap">
        <div class="disclaimer-box">
          <p class="disclaimer-title">{{ $t('halachicUnits.disclaimer.title') }}</p>
          <p class="disclaimer-body">{{ $t('halachicUnits.disclaimer.body1') }}</p>
          <p class="disclaimer-body">{{ $t('halachicUnits.disclaimer.body2') }}</p>
          <p class="disclaimer-body">{{ $t('halachicUnits.disclaimer.body3') }}</p>
          <button class="disclaimer-btn" @click="settings.acceptMidotDisclaimer()">
            {{ $t('halachicUnits.disclaimer.accept') }}
          </button>
        </div>
      </div>

      <div v-else class="converter">
        <div class="conv-stack">
          <!-- From block -->
          <div class="conv-block">
            <AppSelect v-model="fromUnit" class="unit-select" :options="unitOptions" />
            <input
              v-model="inputValue"
              class="value-input"
              type="number"
              inputmode="decimal"
              placeholder="0"
            />
            <div v-if="fromMetric" class="metric-hint">≈ {{ fromMetric }}</div>
            <div v-if="fromUnitInfo?.disputed" class="unit-disputed">
              {{ fromUnitInfo.disputed }}
            </div>
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
            <AppSelect v-model="toUnit" class="unit-select" :options="unitOptions" />
            <div class="result-value">{{ result || '—' }}</div>
            <div v-if="toMetricDisplay" class="metric-hint">≈ {{ toMetricDisplay }}</div>
            <div v-if="toUnitInfo?.disputed" class="unit-disputed">{{ toUnitInfo.disputed }}</div>
          </div>
        </div>
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

      <template v-if="settings.midotDisclaimerAccepted && hasMetric" #footer>
        <div class="options-row">
          <AppSelect v-model="opinion" class="opinion-select" :options="opinionOptions" />
          <button
            class="rounded-toggle"
            :class="{ active: useRounded }"
            :title="useRounded ? $t('halachicUnits.options.roundedTitle') : $t('halachicUnits.options.exactTitle')"
            @click="useRounded = !useRounded"
          >
            {{ useRounded ? $t('halachicUnits.options.rounded') : $t('halachicUnits.options.exact') }}
          </button>
        </div>
      </template>
    </PageCard>
  </div>
</template>

<style scoped>
.midot-page {
  /* Label/control sizes shrink with window height */
  --label-size: clamp(11px, 2.2vh, 14px);
  --hint-size: clamp(10px, 1.9vh, 12px);
  --control-height: clamp(22px, 4.5vh, 30px);
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}

/* Header — measurement systems as chips, options underneath */
.systems {
  container-type: inline-size;
  padding: clamp(4px, 2vh, 16px) clamp(10px, 4vw, 20px);
}
/* Columns divide the 6 systems evenly: one row of 6, else rows of 3, else rows of 2.
   Breakpoints = N pills at the 64px minimum + gaps. */
.systems-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 6px;
}
@container (max-width: 413px) {
  .systems-grid {
    grid-template-columns: repeat(3, 1fr);
  }
}
@container (max-width: 203px) {
  .systems-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
.system-chip {
  /* Equal share of the row, never narrower than the shared minimum */
  min-width: 64px;
  height: clamp(22px, 4.5vh, 30px);
  padding: 0 12px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 15px;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--label-size);
  cursor: pointer;
  white-space: nowrap;
}
.system-chip:hover {
  color: var(--text-primary);
  border-color: var(--text-secondary);
}
.system-chip.active {
  background: color-mix(in srgb, var(--accent-color) 15%, var(--bg-primary));
  border-color: var(--accent-color);
  color: var(--accent-color);
  font-weight: 600;
}
.options-row {
  display: flex;
  gap: 8px;
  padding: clamp(3px, 1.5vh, 12px) clamp(10px, 4vw, 20px);
}
/* Opinion dropdown and rounding toggle share the row equally */
.opinion-select,
.rounded-toggle {
  flex: 1 1 0;
  min-width: 0;
}
.opinion-select :deep(.app-select-label) {
  text-align: center;
}
.rounded-toggle {
  height: var(--control-height);
  padding: 0 12px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--label-size);
  cursor: pointer;
  white-space: nowrap;
}
.rounded-toggle.active {
  background: color-mix(in srgb, var(--accent-color) 15%, var(--bg-primary));
  border-color: var(--accent-color);
  color: var(--accent-color);
}

/* Converter */
.converter {
  display: flex;
  flex-direction: column;
  gap: clamp(1px, 0.6vh, 4px);
  padding: clamp(4px, 2.5vh, 20px) clamp(10px, 4vw, 20px);
}
.conv-block {
  display: flex;
  flex-direction: column;
  gap: clamp(0px, 0.5vh, 4px);
  padding: clamp(3px, 1.5vh, 12px) 12px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}
.conv-block:focus-within {
  border-color: var(--accent-color);
}
.opinion-select,
.unit-select {
  height: var(--control-height);
  font-size: var(--label-size);
}
.unit-select {
  width: 100%;
  background: var(--bg-secondary);
}
.value-input,
.result-value {
  width: 100%;
  height: clamp(20px, 7vh, 52px);
  padding: 0 4px;
  box-sizing: border-box;
  font-size: clamp(14px, 4.5vh, 30px);
  font-family: inherit;
  direction: ltr;
  text-align: start;
}
.value-input {
  background: none;
  border: none;
  outline: none;
  color: var(--text-primary);
}
.value-input::-webkit-outer-spin-button,
.value-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
}
.result-value {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  color: var(--accent-color);
  font-weight: 600;
  overflow: hidden;
  white-space: nowrap;
}
.metric-hint {
  padding: 0 4px;
  font-size: var(--hint-size);
  color: var(--text-secondary);
  direction: ltr;
  text-align: end;
}
.unit-disputed {
  padding: 0 4px;
  font-size: var(--hint-size);
  color: color-mix(in srgb, var(--accent-color) 80%, var(--text-secondary));
}

/* Rows: box · swap · box — equal 1fr rows keep both boxes the same height */
.conv-stack {
  display: grid;
  grid-template-rows: 1fr auto 1fr;
  gap: 4px;
}

/* Swap — centred between the two blocks */
.swap-row {
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
  width: clamp(20px, 4.5vh, 32px);
  height: clamp(20px, 4.5vh, 32px);
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 50%;
  color: var(--text-secondary);
  cursor: pointer;
}
.swap-btn:hover {
  color: var(--accent-color);
  border-color: var(--accent-color);
}

/* Footer — how the result was calculated */
.explanation-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  padding: 8px 2px 0;
  font-size: var(--hint-size);
  color: var(--text-secondary);
}
.explanation-calc {
  font-weight: 600;
  color: var(--text-primary);
}
.explanation-sep {
  opacity: 0.4;
}
.source-link {
  color: var(--accent-color);
  text-decoration: none;
}
.source-link:hover {
  text-decoration: underline;
}

/* Disclaimer */
.disclaimer-wrap {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px 20px;
}
.disclaimer-box {
  max-width: 360px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.disclaimer-title {
  margin: 0;
  font-size: 15px;
  font-weight: 700;
  color: var(--text-primary);
}
.disclaimer-body {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--text-secondary);
}
.disclaimer-btn {
  margin-top: 4px;
  height: 36px;
  background: var(--accent-color);
  color: #fff;
  border: none;
  border-radius: 6px;
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}
.disclaimer-btn:hover {
  opacity: 0.9;
}
</style>
