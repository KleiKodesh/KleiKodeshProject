<script setup lang="ts">
import { ref } from 'vue'
import FontSelectorCmp from './FontSelector.vue'
import SliderSetting from './SliderSetting.vue'

const props = defineProps<{
  availableFonts: string[]
  headerFont: string
  textFont: string
  fontSize: number
  linePadding: number
}>()

const emit = defineEmits<{
  'update:headerFont': [string]
  'update:textFont': [string]
  'update:fontSize': [number]
  'update:linePadding': [number]
  closeOther: []
}>()

const headerFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)
const textFontRef = ref<InstanceType<typeof FontSelectorCmp> | null>(null)

function closeDropdowns(except?: 'header' | 'text') {
  if (except !== 'header' && headerFontRef.value) headerFontRef.value.isOpen = false
  if (except !== 'text' && textFontRef.value) textFontRef.value.isOpen = false
}

defineExpose({ closeDropdowns })
</script>

<template>
  <FontSelectorCmp ref="headerFontRef" label="גופן כותרות"
    :model-value="headerFont" :available-fonts="availableFonts" font-type="sans-serif"
    @update:model-value="emit('update:headerFont', $event)"
    @toggle="closeDropdowns('header'); emit('closeOther')" />
  <FontSelectorCmp ref="textFontRef" label="גופן טקסט"
    :model-value="textFont" :available-fonts="availableFonts" font-type="serif"
    @update:model-value="emit('update:textFont', $event)"
    @toggle="closeDropdowns('text'); emit('closeOther')" />
  <SliderSetting label="גודל גופן" :model-value="fontSize" :min="50" :max="200" :step="5" suffix="%"
    @update:model-value="emit('update:fontSize', $event)" />
  <SliderSetting label="ריווח בין שורות" :model-value="linePadding" :min="1.2" :max="3.0" :step="0.1"
    @update:model-value="emit('update:linePadding', $event)" />
</template>
