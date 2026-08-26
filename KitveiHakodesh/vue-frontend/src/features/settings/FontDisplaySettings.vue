<script setup lang="ts">
import { ref } from 'vue'

defineOptions({ inheritAttrs: false })
import FontSelectorCmp from './FontSelector.vue'
import SliderSetting from './SliderSetting.vue'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'

const props = defineProps<{
  headerFont: string
  textFont: string
  fontSize: number
  /** Optional: a caller that does not offer the weight slider yet renders at 400. */
  fontWeight?: number
  linePadding: number
  /**
   * Exact line spacing. This setting is global (one attribute on <html>) while this
   * component renders once per pane, so only the book instance shows the control —
   * gated on the explicit flag below rather than on `fixedLineHeight === undefined`,
   * because Vue casts an absent Boolean prop to `false`, never `undefined`.
   */
  fixedLineHeight?: boolean
  showFixedLineHeight?: boolean
  /**
   * Body font for books that carry cantillation marks. Only the book instance offers
   * it (the commentary instance leaves both undefined) — teamim appear in the book
   * text, not in the commentaries.
   */
  teamimTextFont?: string
  showTeamimFont?: boolean
  /** Default family per picker, badged in its list. Bare family names. */
  defaultHeaderFont?: string
  defaultTextFont?: string
  defaultTeamimFont?: string
}>()

/**
 * Names for the weights the slider stops on — the number alone reads as arbitrary.
 * Falls back to the bare number for a step this map does not cover.
 *
 * The range stops at 700: Heebo is variable and keeps thickening
 * past it, but Taamey Frank CLM ships as two STATIC faces (regular + bold), so a
 * te'amim book only ever renders two of these steps — 300-500 all look regular and
 * 600-700 both look bold. Offering 800/900 would add steps nothing distinguishes.
 */
const WEIGHT_LABELS: Record<number, string> = {
  300: 'דק',
  400: 'רגיל',
  500: 'בינוני',
  600: 'חצי מודגש',
  700: 'מודגש',
}

function formatWeight(value: number): string {
  const name = WEIGHT_LABELS[value]
  return name ? `${name} (${value})` : String(value)
}

const emit = defineEmits<{
  'update:headerFont': [string]
  'update:textFont': [string]
  'update:teamimTextFont': [string]
  'update:fontSize': [number]
  'update:fontWeight': [number]
  'update:linePadding': [number]
  'update:fixedLineHeight': [boolean]
  closeOther: []
}>()

const headerFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)
const textFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)
const teamimFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)

function closeDropdowns(except?: 'header' | 'text' | 'teamim') {
  if (except !== 'header' && headerFontRef.value) headerFontRef.value.isOpen = false
  if (except !== 'text' && textFontRef.value) textFontRef.value.isOpen = false
  if (except !== 'teamim' && teamimFontRef.value) teamimFontRef.value.isOpen = false
}

defineExpose({ closeDropdowns })

function onHeaderToggle() {
  closeDropdowns('header')
  emit('closeOther')
}

function onTextToggle() {
  closeDropdowns('text')
  emit('closeOther')
}

function onTeamimToggle() {
  closeDropdowns('teamim')
  emit('closeOther')
}
</script>

<template>
  <div v-bind="$attrs">
    <FontSelectorCmp
      ref="headerFontRef"
      label="גופן כותרות"
      hint="הגופן שישמש לכותרות הפרקים והסעיפים"
      :model-value="headerFont"
      font-type="sans-serif"
      :default-font="defaultHeaderFont"
      @update:model-value="emit('update:headerFont', $event)"
      @toggle="onHeaderToggle"
    />
    <FontSelectorCmp
      ref="textFontRef"
      label="גופן גוף הטקסט"
      hint="הגופן שישמש לגוף הטקסט של הספר"
      :model-value="textFont"
      font-type="serif"
      :default-font="defaultTextFont"
      @update:model-value="emit('update:textFont', $event)"
      @toggle="onTextToggle"
    />
    <FontSelectorCmp
      v-if="showTeamimFont"
      ref="teamimFontRef"
      label="גופן ספרים עם טעמים"
      hint="הגופן שישמש לגוף הטקסט בספרים שהטקסט שלהם מנוקד בטעמי המקרא"
      :model-value="teamimTextFont ?? ''"
      font-type="serif"
      :default-font="defaultTeamimFont"
      @update:model-value="emit('update:teamimTextFont', $event)"
      @toggle="onTeamimToggle"
    />
    <SliderSetting
      label="גודל גופן"
      hint="גודל הטקסט ביחס לברירת המחדל"
      :model-value="fontSize"
      :min="50"
      :max="200"
      :step="5"
      suffix="%"
      @update:model-value="emit('update:fontSize', $event)"
    />
    <SliderSetting
      label="עובי גופן"
      hint="עובי הטקסט. בספרים עם טעמים יש שתי דרגות בלבד — רגיל ומודגש"
      :model-value="fontWeight ?? 400"
      :min="300"
      :max="700"
      :step="100"
      :format-value="formatWeight"
      @update:model-value="emit('update:fontWeight', $event)"
    />
    <SliderSetting
      label="ריווח בין שורות"
      hint="המרחק האנכי בין שורות הטקסט"
      :model-value="linePadding"
      :min="1.2"
      :max="3.0"
      :step="0.1"
      @update:model-value="emit('update:linePadding', $event)"
    />
    <SettingRow
      v-if="showFixedLineHeight"
      label="אופן חישוב הריווח"
      hint="'מדוייק' שומר על ריווח זהה בין כל השורות, גם כשיש מילה גדולה באמצע השורה. שים לב: מילה גדולה במיוחד עלולה לחפוף לשורה שמעליה"
    >
      <ToggleGroup
        :model-value="fixedLineHeight"
        :options="[
          { label: 'רגיל', value: false },
          { label: 'מדוייק', value: true },
        ]"
        @update:model-value="emit('update:fixedLineHeight', $event)"
      />
    </SettingRow>
  </div>
</template>
