<template>
  <div class="tab-pane">
    <!-- Section: BookLineView Settings -->
    <div class="section-header">הגדרות תצוגת ספר</div>

    <FontSelector
      ref="headerFontRef"
      label="גופן כותרות"
      v-model="headerFont"
      :available-fonts="availableFonts"
      font-type="sans-serif"
      @toggle="closeOtherDropdowns('header')"
    />

    <FontSelector
      ref="textFontRef"
      label="גופן טקסט"
      v-model="textFont"
      :available-fonts="availableFonts"
      font-type="serif"
      @toggle="closeOtherDropdowns('text')"
    />

    <SliderSetting
      label="גודל גופן"
      v-model="fontSize"
      :min="50"
      :max="200"
      :step="5"
      suffix="%"
    />

    <SliderSetting
      label="ריווח שורות"
      v-model="linePadding"
      :min="1.2"
      :max="3.0"
      :step="0.1"
    />

    <!-- Section: Commentary Settings -->
    <div class="section-header">הגדרות תצוגת פירושים</div>

    <FontSelector
      ref="commentaryHeaderFontRef"
      label="גופן כותרות"
      v-model="commentaryHeaderFont"
      :available-fonts="availableFonts"
      font-type="sans-serif"
      @toggle="closeOtherDropdowns('commentaryHeader')"
    />

    <FontSelector
      ref="commentaryTextFontRef"
      label="גופן טקסט"
      v-model="commentaryTextFont"
      :available-fonts="availableFonts"
      font-type="serif"
      @toggle="closeOtherDropdowns('commentaryText')"
    />

    <SliderSetting
      label="גודל גופן"
      v-model="commentaryFontSize"
      :min="50"
      :max="200"
      :step="5"
      suffix="%"
    />

    <SliderSetting
      label="ריווח שורות"
      v-model="commentaryLinePadding"
      :min="1.2"
      :max="3.0"
      :step="0.1"
    />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import FontSelector from '@/components/settings/FontSelector.vue'
import SliderSetting from '@/components/settings/SliderSetting.vue'

const settingsStore = useSettingsStore()
const {
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding
} = storeToRefs(settingsStore)

const availableFonts = ref<string[]>([])

const headerFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
const textFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
const commentaryHeaderFontRef = ref<InstanceType<typeof FontSelector> | null>(null)
const commentaryTextFontRef = ref<InstanceType<typeof FontSelector> | null>(null)

const closeOtherDropdowns = (except: string) => {
  if (except !== 'header' && headerFontRef.value) {
    headerFontRef.value.isOpen = false
  }
  if (except !== 'text' && textFontRef.value) {
    textFontRef.value.isOpen = false
  }
  if (except !== 'commentaryHeader' && commentaryHeaderFontRef.value) {
    commentaryHeaderFontRef.value.isOpen = false
  }
  if (except !== 'commentaryText' && commentaryTextFontRef.value) {
    commentaryTextFontRef.value.isOpen = false
  }
}

const detectFonts = async () => {
  const fonts = [
    'Arial',
    'Times New Roman',
    'Courier New',
    'Georgia',
    'Verdana',
    'Tahoma',
    'Trebuchet MS',
    'Comic Sans MS',
    'Impact',
    'Lucida Console',
    'Segoe UI',
    'Calibri',
    'Cambria',
    'Candara',
    'Consolas',
    'Constantia',
    'Corbel',
    'David',
    'Frank Ruehl',
    'Gisha',
    'Leelawadee',
    'Levenim MT',
    'Miriam',
    'Narkisim',
    'Rod',
    'Keter YG',
    'Shofar',
    'Simple CLM',
    'Ezra SIL',
    'SBL Hebrew',
    'Cardo',
    'Taamey David CLM',
    'Taamey Frank CLM',
    'Taamey Ashkenaz',
    'Keter YG',
    'Shofar',
    'Hadasim CLM',
    'Drugulin CLM',
    'Aharoni',
    'Miriam Fixed',
    'Miriam Mono CLM'
  ]

  const canvas = document.createElement('canvas')
  const context = canvas.getContext('2d')
  if (!context) return

  const baseFonts = ['monospace', 'sans-serif', 'serif']
  const testString = 'mmmmmmmmmmlli'
  const testSize = '72px'

  const baseWidths: Record<string, number> = {}
  for (const baseFont of baseFonts) {
    context.font = `${testSize} ${baseFont}`
    baseWidths[baseFont] = context.measureText(testString).width
  }

  const detected: string[] = []
  for (const font of fonts) {
    let isDetected = false
    for (const baseFont of baseFonts) {
      context.font = `${testSize} '${font}', ${baseFont}`
      const width = context.measureText(testString).width
      if (width !== baseWidths[baseFont]) {
        isDetected = true
        break
      }
    }
    if (isDetected) {
      detected.push(font)
    }
  }

  availableFonts.value = detected
}

onMounted(() => {
  detectFonts()
})
</script>

<style scoped>
.tab-pane {
  direction: rtl;
}

.section-header {
  padding: 16px 16px 12px;
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border-bottom: 2px solid var(--border-color);
}

.setting-group {
  padding: 14px 16px;
  border-bottom: 1px solid var(--border-color);
}

.setting-group:last-child {
  border-bottom: none;
}

.setting-label {
  font-size: 14px;
  margin-bottom: 10px;
}
</style>
