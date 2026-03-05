<template>
  <div class="tab-pane">
    <!-- Section: BookLineView Settings -->
    <div class="section-header">הגדרות תצוגת ספר</div>

    <FontSelector ref="headerFontRef"
                  label="גופן כותרות"
                  v-model="headerFont"
                  :available-fonts="availableFonts"
                  font-type="sans-serif"
                  @toggle="closeOtherDropdowns('header')" />

    <FontSelector ref="textFontRef"
                  label="גופן טקסט"
                  v-model="textFont"
                  :available-fonts="availableFonts"
                  font-type="serif"
                  @toggle="closeOtherDropdowns('text')" />

    <SliderSetting label="גודל גופן"
                   v-model="fontSize"
                   :min="50"
                   :max="200"
                   :step="5"
                   suffix="%" />

    <SliderSetting label="ריווח שורות"
                   v-model="linePadding"
                   :min="1.2"
                   :max="3.0"
                   :step="0.1" />

    <!-- Section: Commentary Settings -->
    <div class="section-header">הגדרות תצוגת פירושים</div>

    <FontSelector ref="commentaryHeaderFontRef"
                  label="גופן כותרות"
                  v-model="commentaryHeaderFont"
                  :available-fonts="availableFonts"
                  font-type="sans-serif"
                  @toggle="closeOtherDropdowns('commentaryHeader')" />

    <FontSelector ref="commentaryTextFontRef"
                  label="גופן טקסט"
                  v-model="commentaryTextFont"
                  :available-fonts="availableFonts"
                  font-type="serif"
                  @toggle="closeOtherDropdowns('commentaryText')" />

    <SliderSetting label="גודל גופן"
                   v-model="commentaryFontSize"
                   :min="50"
                   :max="200"
                   :step="5"
                   suffix="%" />

    <SliderSetting label="ריווח שורות"
                   v-model="commentaryLinePadding"
                   :min="1.2"
                   :max="3.0"
                   :step="0.1" />
  </div>
</template>

<script setup lang="ts">
import FontSelector from '@/components/settings/FontSelector.vue'
import SliderSetting from '@/components/settings/SliderSetting.vue'
import { useReadingSettingsTab } from '@/components/settings/useReadingSettingsTab'

const {
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  availableFonts,
  headerFontRef,
  textFontRef,
  commentaryHeaderFontRef,
  commentaryTextFontRef,
  closeOtherDropdowns
} = useReadingSettingsTab()
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
