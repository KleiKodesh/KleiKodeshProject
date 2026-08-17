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
  linePadding: number
  /**
   * Exact line spacing. This setting is global (one attribute on <html>) while this
   * component renders once per pane, so only the book instance shows the control —
   * gated on the explicit flag below rather than on `fixedLineHeight === undefined`,
   * because Vue casts an absent Boolean prop to `false`, never `undefined`.
   */
  fixedLineHeight?: boolean
  showFixedLineHeight?: boolean
}>()

const emit = defineEmits<{
  'update:headerFont': [string]
  'update:textFont': [string]
  'update:fontSize': [number]
  'update:linePadding': [number]
  'update:fixedLineHeight': [boolean]
  closeOther: []
}>()

const headerFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)
const textFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)

function closeDropdowns(except?: 'header' | 'text') {
  if (except !== 'header' && headerFontRef.value) headerFontRef.value.isOpen = false
  if (except !== 'text' && textFontRef.value) textFontRef.value.isOpen = false
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
</script>

<template>
  <div v-bind="$attrs">
    <FontSelectorCmp
      ref="headerFontRef"
      label="גופן כותרות"
      hint="הגופן שישמש לכותרות הפרקים והסעיפים"
      :model-value="headerFont"
      font-type="sans-serif"
      @update:model-value="emit('update:headerFont', $event)"
      @toggle="onHeaderToggle"
    />
    <FontSelectorCmp
      ref="textFontRef"
      label="גופן טקסט"
      hint="הגופן שישמש לגוף הטקסט של הספר"
      :model-value="textFont"
      font-type="serif"
      @update:model-value="emit('update:textFont', $event)"
      @toggle="onTextToggle"
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
      label="מרווח בין שורות"
      hint="מרווח מדוייק שומר על ריווח זהה בין כל השורות, גם כשיש מילה גדולה באמצע השורה. שים לב: מילה גדולה במיוחד עלולה לחפוף לשורה שמעליה"
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
